using System;
using System.Linq;
using System.Text;
using LightBulb.PlatformInterop;
using Xunit;

namespace LightBulb.Core.Tests;

public class MonitorIdentitySpecs
{
    [Fact]
    public void A_serial_number_keeps_the_same_profile_across_connectors()
    {
        var edid = MakeEdid(123456);
        Assert.Equal(MonitorIdentity.GetId(edid, "HDMI"), MonitorIdentity.GetId(edid, "USB-C"));
    }

    [Fact]
    public void Monitors_without_a_serial_are_kept_separate()
    {
        var edid = MakeEdid(0);
        Assert.Equal("HDMI", MonitorIdentity.GetId(edid, "HDMI"));
        Assert.Equal("USB-C", MonitorIdentity.GetId(edid, "USB-C"));
    }

    [Fact]
    public void Different_serial_numbers_do_not_share_a_profile() =>
        Assert.NotEqual(
            MonitorIdentity.GetId(MakeEdid(1), "HDMI"),
            MonitorIdentity.GetId(MakeEdid(2), "HDMI")
        );

    [Fact]
    public void A_text_serial_and_friendly_name_are_supported()
    {
        var edid = MakeEdid(0);
        edid[57] = 0xFF;
        Encoding.ASCII.GetBytes("OLED-12345   ").CopyTo(edid, 59);
        edid[75] = 0xFC;
        Encoding.ASCII.GetBytes("My OLED      ").CopyTo(edid, 77);
        Checksum(edid);
        Assert.Contains("OLED-12345", MonitorIdentity.GetId(edid, "HDMI"));
        Assert.Equal("My OLED", MonitorIdentity.GetName(edid));
    }

    [Fact]
    public void Corrupt_or_missing_edid_falls_back_to_the_connection()
    {
        Assert.Equal("HDMI", MonitorIdentity.GetId(null, "HDMI"));
        var edid = MakeEdid(123);
        edid[20]++;
        Assert.Equal("HDMI", MonitorIdentity.GetId(edid, "HDMI"));
    }

    private static byte[] MakeEdid(uint serial)
    {
        var edid = new byte[128];
        new byte[] { 0, 255, 255, 255, 255, 255, 255, 0 }.CopyTo(edid, 0);
        edid[8] = 0x26;
        edid[9] = 0xA3;
        edid[10] = 1;
        BitConverter.GetBytes(serial).CopyTo(edid, 12);
        Checksum(edid);
        return edid;
    }

    private static void Checksum(byte[] edid) =>
        edid[127] = (byte)((256 - edid.Take(127).Sum(b => (int)b) % 256) % 256);
}
