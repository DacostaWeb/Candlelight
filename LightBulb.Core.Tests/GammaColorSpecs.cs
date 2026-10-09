using Xunit;

namespace LightBulb.Core.Tests;

public class GammaColorSpecs
{
    [Theory]
    [InlineData(510, 500)]
    [InlineData(500, 510)]
    [InlineData(1910, 1900)]
    [InlineData(1900, 1910)]
    public void Zero_boundary_changes_require_an_update(double previous, double next)
    {
        Assert.True(GammaColor.RequiresUpdate(new(previous, 1), new(next, 1)));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.01)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void Five_hundred_kelvin_suppresses_green_and_blue(double brightness)
    {
        var color = GammaColor.FromConfiguration(new(500, brightness));
        Assert.Equal(brightness, color.Red);
        Assert.Equal(0, color.Green);
        Assert.Equal(0, color.Blue);
    }

    [Fact]
    public void Quantized_zero_boundary_includes_brightness()
    {
        Assert.True(GammaColor.RequiresUpdate(new(6600, 0.000001), new(6600, 0.00002)));
    }

    [Fact]
    public void Small_changes_without_zero_crossings_remain_insignificant()
    {
        Assert.False(GammaColor.RequiresUpdate(new(2500, 1), new(2490, 0.995)));
    }
}
