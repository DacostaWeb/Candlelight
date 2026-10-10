namespace Candlelight.Engine;

internal sealed record MonitorRenderPlan(
    DisplayDescriptor Display,
    ColorProfile Profile,
    bool Enabled,
    ChannelGain Gain,
    ChannelGain LocalGain
)
{
    public bool NeedsSurface => LocalGain != new ChannelGain(1, 1, 1);
}

/// <summary>
/// Factor each monitor's gain into a shared desktop matrix and a local correction.
/// The shared gain never removes a channel needed by another monitor; local
/// corrections only attenuate, avoiding clipping and division by zero.
/// </summary>
internal sealed record DesktopRenderPlan(
    ChannelGain? DesktopGain,
    IReadOnlyList<MonitorRenderPlan> Monitors
)
{
    public static DesktopRenderPlan Create(
        IReadOnlyList<DisplayDescriptor> displays,
        IReadOnlyDictionary<string, (ColorProfile Profile, bool Enabled)> profiles,
        bool allowDesktopEffect
    )
    {
        var targets = displays
            .Select(display =>
            {
                var saved = profiles.GetValueOrDefault(display.Id);
                var profile = saved.Profile ?? ColorProfile.Normal;
                return (
                    Display: display,
                    Profile: profile,
                    saved.Enabled,
                    Gain: saved.Enabled
                        ? ChannelGain.FromProfile(profile)
                        : new ChannelGain(1, 1, 1)
                );
            })
            .ToArray();
        var shared =
            allowDesktopEffect && targets.Any(t => t.Enabled)
                ? new ChannelGain(
                    targets.Max(t => t.Gain.Red),
                    targets.Max(t => t.Gain.Green),
                    targets.Max(t => t.Gain.Blue)
                )
                : null;
        var baseline = shared ?? new(1, 1, 1);
        static double Relative(double target, double common) => common == 0 ? 1 : target / common;
        return new(
            shared,
            targets
                .Select(t => new MonitorRenderPlan(
                    t.Display,
                    t.Profile,
                    t.Enabled,
                    t.Gain,
                    new(
                        Relative(t.Gain.Red, baseline.Red),
                        Relative(t.Gain.Green, baseline.Green),
                        Relative(t.Gain.Blue, baseline.Blue)
                    )
                ))
                .ToArray()
        );
    }

    public MonitorRenderPlan? At(int x, int y) =>
        Monitors.FirstOrDefault(m =>
            x >= m.Display.Left
            && x < m.Display.Left + m.Display.Width
            && y >= m.Display.Top
            && y < m.Display.Top + m.Display.Height
        );
}
