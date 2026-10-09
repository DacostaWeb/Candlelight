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

    public bool Apply(GammaRamp ramp)
    {
        FailureReason = null;
        // The LUT can outlive a process or have been applied by a diagnostic
        // helper. GDI readback alone cannot see that independent color layer.
        var activeColorLayer =
            api.IsColorSystemAvailable
            && api.ReadColorSystem(out var existingColor)
            && !GammaRamp.Identity().Matches(existingColor);
        if (!UsesColorSystem && !activeColorLayer)
        {
            if (api.WriteGdi(ref ramp) && api.ReadGdi(out var actual) && ramp.Matches(actual))
                return true;
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

        // Apply the desired filter FIRST, then neutralize GDI. This prevents a
        // daylight frame and stops the two independent LUTs multiplying together.
        var identity = GammaRamp.Identity();
        if (!api.WriteGdi(ref identity))
        {
            FailureReason = "Não foi possível neutralizar a gama anterior neste ecrã.";
            return false;
        }
        if (!api.ReadColorSystem(out var color) || !ramp.Matches(color))
        {
            FailureReason = "O Windows não confirmou a cor aplicada neste ecrã.";
            return false;
        }
        return true;
    }

    public void Reset()
    {
        var identity = GammaRamp.Identity();
        if (UsesColorSystem)
            api.WriteColorSystem(ref identity);
        api.WriteGdi(ref identity);
    }
}
