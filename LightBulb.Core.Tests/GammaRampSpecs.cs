using LightBulb.PlatformInterop.Internal;
using Xunit;

namespace LightBulb.Core.Tests;

public class GammaRampSpecs
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Refresh_preserves_zero_channels(int offset)
    {
        var ramp = GammaRamp.Create(1, 0, 0, offset);
        Assert.All(ramp.Green, value => Assert.Equal((ushort)0, value));
        Assert.All(ramp.Blue, value => Assert.Equal((ushort)0, value));
        Assert.Equal((ushort)(65025 + offset), ramp.Red[255]);
    }

    [Fact]
    public void Refresh_preserves_quantized_black_at_tiny_brightness()
    {
        var ramp = GammaRamp.Create(0.000001, 0.000001, 0.000001, 4);
        Assert.All(ramp.Red, value => Assert.Equal((ushort)0, value));
        Assert.All(ramp.Green, value => Assert.Equal((ushort)0, value));
        Assert.All(ramp.Blue, value => Assert.Equal((ushort)0, value));
    }
}
