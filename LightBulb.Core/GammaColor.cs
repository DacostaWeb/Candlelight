using System;

namespace LightBulb.Core;

public readonly record struct GammaColor(double Red, double Green, double Blue)
{
    private int ZeroChannels =>
        ((ushort)(65025 * Red) == 0 ? 1 : 0)
        | ((ushort)(65025 * Green) == 0 ? 2 : 0)
        | ((ushort)(65025 * Blue) == 0 ? 4 : 0);

    public static GammaColor FromConfiguration(ColorConfiguration configuration)
    {
        var temperature = configuration.Temperature;
        var red =
            temperature > 6600
                ? Math.Clamp(
                    Math.Pow(temperature / 100 - 60, -0.1332047592) * 329.698727446 / 255,
                    0,
                    1
                )
                : 1;
        var green =
            temperature > 6600
                ? Math.Clamp(
                    Math.Pow(temperature / 100 - 60, -0.0755148492) * 288.1221695283 / 255,
                    0,
                    1
                )
                : Math.Clamp(
                    (Math.Log(temperature / 100) * 99.4708025861 - 161.1195681661) / 255,
                    0,
                    1
                );
        var blue =
            temperature >= 6600 ? 1
            : temperature <= 1900 ? 0
            : Math.Clamp(
                (Math.Log(temperature / 100 - 10) * 138.5177312231 - 305.0447927307) / 255,
                0,
                1
            );
        return new(
            red * configuration.Brightness,
            green * configuration.Brightness,
            blue * configuration.Brightness
        );
    }

    public static bool RequiresUpdate(
        ColorConfiguration previous,
        ColorConfiguration next,
        double temperatureThreshold = 15,
        double brightnessThreshold = 0.01
    ) =>
        Math.Abs(next.Temperature - previous.Temperature) >= temperatureThreshold
        || Math.Abs(next.Brightness - previous.Brightness) >= brightnessThreshold
        || FromConfiguration(previous).ZeroChannels != FromConfiguration(next).ZeroChannels;
}
