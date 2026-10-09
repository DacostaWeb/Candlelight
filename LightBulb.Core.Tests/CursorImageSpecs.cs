using System;
using System.Linq;
using System.Runtime.InteropServices;
using Candlelight.Engine;
using FluentAssertions;
using Xunit;

namespace Candlelight.Engine.Tests;

public class CursorImageSpecs
{
    [Fact]
    public void Red_cursor_preserves_alpha_and_source_pixels()
    {
        byte[] pixels = [255, 255, 255, 255, 40, 80, 120, 128, 0, 0, 0, 0];
        var image = new CursorImage(3, 1, 1, 0, pixels, new byte[4]);
        var output = image.Tint(new(0.15, 0, 0));
        output.Should().Equal(0, 0, 38, 255, 0, 0, 18, 128, 0, 0, 0, 0);
        image.Pixels.Should().Equal(255, 255, 255, 255, 40, 80, 120, 128, 0, 0, 0, 0);
        image.HotspotX.Should().Be(1);
    }

    [Fact]
    public void Warm_cursor_preserves_black_outline_and_transparency()
    {
        var image = new CursorImage(
            3,
            1,
            0,
            0,
            [255, 255, 255, 255, 0, 0, 0, 255, 0, 0, 0, 0],
            new byte[4]
        );
        var gain = ChannelGain.FromProfile(new(ColorMode.Temperature, 2700, 1));
        var output = image.Tint(gain);
        output[0].Should().BeLessThan(output[1]);
        output[1].Should().BeLessThan(output[2]);
        output.Skip(4).Should().Equal(0, 0, 0, 255, 0, 0, 0, 0);
    }

    [Fact]
    public void Inverting_monochrome_stroke_gets_a_visible_outline()
    {
        // 5x3, DWORD-aligned AND plane all transparent; middle XOR pixel inverts.
        byte[] mask =
        [
            0xF8,
            0,
            0,
            0,
            0xF8,
            0,
            0,
            0,
            0xF8,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0x20,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
        ];
        var pixels = CursorImage.Monochrome(5, 3, mask);
        pixels.Skip((1 * 5 + 2) * 4).Take(4).Should().Equal(255, 255, 255, 255);
        pixels.Skip((1 * 5 + 1) * 4).Take(4).Should().Equal(0, 0, 0, 255);
        pixels.Take(4).Should().Equal(0, 0, 0, 0);
        var action = () => CursorImage.Monochrome(5, 3, new byte[12]);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Native_bitmap_header_has_the_documented_layout()
    {
        Marshal.SizeOf<CursorNative.BitmapInfo>().Should().Be(48);
        var info = CursorNative.BitmapInfo.For(48, 48, 1);
        info.Size.Should().Be(40);
        info.Height.Should().Be(-48);
        info.White.Should().Be(0x00FFFFFF);
    }
}
