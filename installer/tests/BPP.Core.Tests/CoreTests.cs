using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using BPP.Core;

namespace BPP.Core.Tests;

public class HostListTests
{
    private static NetInterface Lo() =>
        new("Loopback", "Loopback Pseudo-Interface", true, true, new[] { IPAddress.Loopback });

    private static NetInterface If(string name, string desc, bool up, params string[] ips) =>
        new(name, desc, up, false, ips.Select(IPAddress.Parse).ToList());

    [Fact] public void Only_loopback_gives_localhost() => Assert.Equal(new[] { "localhost" }, HostList.From(new[] { Lo() }));

    [Fact]
    public void Skips_wsl_hyperv_apipa_and_down()
    {
        var r = HostList.From(new[]
        {
            If("Wi-Fi", "", true, "192.168.1.50"),
            If("vEthernet (WSL)", "Hyper-V Virtual Ethernet", true, "172.20.0.1"),
            If("Ethernet", "", true, "169.254.3.3"),
            If("Eth2", "", false, "10.0.0.5"),
        });
        Assert.Equal(new[] { "localhost", "192.168.1.50" }, r);
    }

    [Fact]
    public void Sorts_numerically_and_deduplicates()
    {
        var r = HostList.From(new[]
        {
            If("A", "", true, "192.168.1.9", "10.0.0.2"),
            If("B", "", true, "10.0.0.10", "10.0.0.2"),
        });
        Assert.Equal(new[] { "localhost", "10.0.0.2", "10.0.0.10", "192.168.1.9" }, r);
    }

    [Fact] public void Csv() => Assert.Equal("localhost,10.0.0.1", HostList.ToCsv(new[] { "localhost", "10.0.0.1" }));
}

public class BppEventParserTests
{
    [Fact]
    public void Parses_event()
    {
        Assert.True(BppEventParser.TryParse("{\"step\":\"pull\",\"progress\":0.25,\"message\":\"x\"}", out var e));
        Assert.Equal(0.25, e!.Progress);
        Assert.Equal("pull", e.Step);
        Assert.Equal("x", e.Message);
    }

    [Fact]
    public void Null_progress_and_garbage()
    {
        Assert.True(BppEventParser.TryParse("{\"step\":\"migrate\",\"progress\":null,\"message\":\"\"}", out var e));
        Assert.Null(e!.Progress);
        Assert.False(BppEventParser.TryParse("Pulling...", out _));
    }

    [Fact]
    public void Missing_step_or_non_object_is_rejected()
    {
        Assert.False(BppEventParser.TryParse("{\"progress\":0.5}", out _));
        Assert.False(BppEventParser.TryParse("[1,2]", out _));
        Assert.False(BppEventParser.TryParse("", out _));
    }

    [Fact]
    public void Missing_message_defaults_to_empty()
    {
        Assert.True(BppEventParser.TryParse("{\"step\":\"ready\"}", out var e));
        Assert.Equal("", e!.Message);
        Assert.Null(e.Progress);
    }
}

public class WslTextTests
{
    [Fact] public void Decodes_utf16_wsl_list() => Assert.Contains("BPP", WslText.Decode(Encoding.Unicode.GetBytes("Ubuntu\r\nBPP\r\n")).Split('\n'));

    [Fact] public void Decodes_utf8_and_strips_cr_nul() => Assert.Equal("a\nb", WslText.Decode(Encoding.UTF8.GetBytes("a\r\nb\0")));

    [Fact] public void Decodes_utf16_with_bom() => Assert.Equal("BPP", WslText.Decode(new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("BPP")).ToArray()));
}

public class PreflightTests
{
    [Fact] public void Preflight_refuses_7gb() => Assert.Contains(PreflightProblem.TooLittleRam, Preflight.Check(new(7UL << 30, 100L << 30, Architecture.X64, true)));

    [Fact] public void Preflight_ok_at_limits() => Assert.Empty(Preflight.Check(new(8UL << 30, 25L << 30, Architecture.X64, true)));

    [Fact]
    public void Preflight_arm_and_port()
    {
        var p = Preflight.Check(new(16UL << 30, 100L << 30, Architecture.Arm64, false));
        Assert.Contains(PreflightProblem.NotX64, p);
        Assert.Contains(PreflightProblem.PortBusy, p);
    }

    [Fact] public void Preflight_refuses_little_disk() => Assert.Contains(PreflightProblem.TooLittleDisk, Preflight.Check(new(16UL << 30, (25L << 30) - 1, Architecture.X64, true)));
}
