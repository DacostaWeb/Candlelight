namespace LightBulb.PlatformInterop.Internal;

internal interface IGammaRampApi
{
    bool IsColorSystemAvailable { get; }
    bool WriteGdi(ref GammaRamp ramp);
    bool ReadGdi(out GammaRamp ramp);
    bool WriteColorSystem(ref GammaRamp ramp);
    bool ReadColorSystem(out GammaRamp ramp);
}

internal sealed class GammaRampWriter(
    IGammaRampApi api,
    bool useColorSystem = false,
    GammaRampGuard? guard = null
)
{
    private readonly GammaRampGuard _guard = guard ?? new();
    public bool UsesColorSystem { get; private set; } = useColorSystem;
    public string? FailureReason { get; private set; }
    public string LastOperation { get; private set; } = "";
    public bool DidWrite { get; private set; }
    public bool IsDeferred => _guard.IsDeferred;

    public bool IsCurrent(GammaRamp ramp)
    {
        FailureReason = null;
        DidWrite = false;
        var identity = GammaRamp.Identity();
        var color = default(GammaRamp);
        var colorReadable = api.IsColorSystemAvailable && api.ReadColorSystem(out color);
        var activeColorLayer = colorReadable && !identity.Matches(color);
        var gdiReadable = api.ReadGdi(out var gdi);
        LastOperation =
            $"color current={Summary(colorReadable, color)}; GDI current={Summary(gdiReadable, gdi)}";

        if (UsesColorSystem || activeColorLayer)
        {
            if (!colorReadable || !ramp.Matches(color) || !gdiReadable || !identity.Matches(gdi))
                return false;
            // A matching filter may have survived a prior process. Own its reset
            // even when this process did not need to rewrite it.
            UsesColorSystem = true;
        }
        else if (
            (api.IsColorSystemAvailable && !colorReadable)
            || !gdiReadable
            || !ramp.Matches(gdi)
        )
            return false;

        // A fade may cross the requested target briefly. A matching sample
        // does not mean the external owner has finished its transition.
        if (_guard.IsDeferred && _guard.ShouldDefer(gdi, colorReadable ? color : null))
        {
            LastOperation += "; target matches during external transition; waiting; write skipped";
            return false;
        }
        LastOperation += "; verified; write skipped";
        _guard.Remember(gdi, colorReadable ? color : null);
        return true;
    }

    public bool Apply(GammaRamp ramp)
    {
        FailureReason = null;
        DidWrite = false;
        // The LUT can outlive a process or have been applied by a diagnostic
        // helper. GDI readback alone cannot see that independent color layer.
        var existingColor = default(GammaRamp);
        var colorReadable = api.IsColorSystemAvailable && api.ReadColorSystem(out existingColor);
        LastOperation = $"color before={Summary(colorReadable, existingColor)}";
        // An unavailable readback can mean a stale display context or a driver
        // transition. Do not try either write API while we cannot verify it.
        if (
            ((api.IsColorSystemAvailable || UsesColorSystem) && !colorReadable)
            || !api.ReadGdi(out var existingGdi)
        )
        {
            FailureReason = "O Windows não permite verificar a cor deste ecrã neste momento.";
            LastOperation += "; readback unavailable; write skipped";
            return false;
        }
        if (_guard.ShouldDefer(existingGdi, colorReadable ? existingColor : null))
        {
            LastOperation +=
                "; external color change; waiting for 3000 ms of stable readback; write skipped";
            return false;
        }
        var activeColorLayer = colorReadable && !GammaRamp.Identity().Matches(existingColor);
        DidWrite = true;
        if (!UsesColorSystem && !activeColorLayer)
        {
            if (api.WriteGdi(ref ramp) && api.ReadGdi(out var actual) && ramp.Matches(actual))
            {
                LastOperation += "; GDI target confirmed";
                _guard.Remember(actual, colorReadable ? existingColor : null);
                return true;
            }
            if (!api.IsColorSystemAvailable)
            {
                FailureReason =
                    "O Windows bloqueou a gama. O desbloqueio pode exigir terminar sessão ou reiniciar o PC.";
                return false;
            }
        }

        if (!api.WriteColorSystem(ref ramp))
        {
            FailureReason = "O sistema de cor do Windows recusou os valores pedidos.";
            return false;
        }
        // Remember any successful write, even if subsequent verification fails:
        // Reset must undo this layer as well as the GDI layer.
        UsesColorSystem = true;
        LastOperation += "; color written";

        // Keep an already neutral GDI layer untouched. Rewriting identity during
        // every wake retry needlessly reprograms a second color pipeline.
        // When needed, install the desired filter FIRST, then neutralize GDI.
        var identity = GammaRamp.Identity();
        if (api.ReadGdi(out var gdi) && identity.Matches(gdi))
            LastOperation += "; GDI identity kept";
        else
        {
            LastOperation += "; GDI neutralization required";
            if (!api.WriteGdi(ref identity) || !api.ReadGdi(out gdi) || !identity.Matches(gdi))
            {
                FailureReason = "Não foi possível neutralizar a gama anterior neste ecrã.";
                return false;
            }
        }
        var colorConfirmed = api.ReadColorSystem(out var color);
        LastOperation += $"; color after={Summary(colorConfirmed, color)}";
        if (!colorConfirmed || !ramp.Matches(color))
        {
            FailureReason = "O Windows não confirmou a cor aplicada neste ecrã.";
            return false;
        }
        _guard.Remember(gdi, color);
        return true;
    }

    private static string Summary(bool readable, GammaRamp ramp) =>
        readable ? $"{ramp.Red[255]}/{ramp.Green[255]}/{ramp.Blue[255]}" : "unavailable";

    public void Reset()
    {
        // The external owner is still changing this output. Closing the app
        // must not introduce another reset in the middle of that transition.
        if (_guard.IsDeferred)
            return;
        var identity = GammaRamp.Identity();
        if (UsesColorSystem)
            api.WriteColorSystem(ref identity);
        api.WriteGdi(ref identity);
        _guard.Clear();
    }
}
