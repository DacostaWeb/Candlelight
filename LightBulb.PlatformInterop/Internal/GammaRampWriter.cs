namespace LightBulb.PlatformInterop.Internal;

internal interface IGammaRampApi
{
    bool IsColorSystemAvailable { get; }
    bool WriteGdi(ref GammaRamp ramp);
    bool ReadGdi(out GammaRamp ramp);
    bool WriteColorSystem(ref GammaRamp ramp);
    bool ReadColorSystem(out GammaRamp ramp);
}

internal sealed class GammaRampWriter(IGammaRampApi api, bool useColorSystem = false)
{
    public bool UsesColorSystem { get; private set; } = useColorSystem;
    public string? FailureReason { get; private set; }
    public string LastOperation { get; private set; } = "";

    public bool Apply(GammaRamp ramp)
    {
        FailureReason = null;
        // The LUT can outlive a process or have been applied by a diagnostic
        // helper. GDI readback alone cannot see that independent color layer.
        var existingColor = default(GammaRamp);
        var colorReadable = api.IsColorSystemAvailable && api.ReadColorSystem(out existingColor);
        LastOperation = $"color before={Summary(colorReadable, existingColor)}";
        var activeColorLayer = colorReadable && !GammaRamp.Identity().Matches(existingColor);
        if (!UsesColorSystem && !activeColorLayer)
        {
            if (api.WriteGdi(ref ramp) && api.ReadGdi(out var actual) && ramp.Matches(actual))
            {
                LastOperation += "; GDI target confirmed";
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
        return true;
    }

    private static string Summary(bool readable, GammaRamp ramp) =>
        readable ? $"{ramp.Red[255]}/{ramp.Green[255]}/{ramp.Blue[255]}" : "unavailable";

    public void Reset()
    {
        var identity = GammaRamp.Identity();
        if (UsesColorSystem)
            api.WriteColorSystem(ref identity);
        api.WriteGdi(ref identity);
    }
}
