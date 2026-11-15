using BPP.Core;

namespace BPP.Core.Tests;

public class StatusReportTests
{
    private static ServiceStatus Svc(string name, string state = "running", string health = "healthy")
        => new(name, state, health);

    private static StackStatus Stack(params ServiceStatus[] services) => new(Docker: true, services);

    [Theory]
    [InlineData("running", "healthy", StatusLevel.Ok)]
    [InlineData("running", "", StatusLevel.Ok)] // no healthcheck
    [InlineData("running", "starting", StatusLevel.Starting)]
    [InlineData("running", "unhealthy", StatusLevel.Bad)]
    [InlineData("restarting", "", StatusLevel.Starting)]
    [InlineData("created", "", StatusLevel.Starting)]
    [InlineData("exited", "", StatusLevel.Bad)]
    [InlineData("absent", "", StatusLevel.Bad)]
    [InlineData("", "", StatusLevel.Bad)]
    public void Service_level(string state, string health, StatusLevel expected)
        => Assert.Equal(expected, Svc("x", state, health).Level);

    [Fact]
    public void Parses_bpp_ctl_status()
    {
        const string json = """
            {"running":true,"health":"healthy","docker":true,"image_tag":"202611.1407","services":[{"name":"dbserver","state":"running","health":"healthy"},{"name":"denorm-queue","state":"running","health":""},{"name":"webserver","state":"absent","health":""}]}
            """;
        Assert.True(StackStatus.TryParse(json, out var s));
        Assert.True(s!.Docker);
        Assert.Equal("202611.1407", s.ImageTag);
        Assert.Equal(new[] { Svc("dbserver"), Svc("denorm-queue", health: ""), Svc("webserver", "absent", "") }, s.Services);
    }

    [Fact]
    public void Parses_docker_down_and_skips_other_lines()
    {
        Assert.True(StackStatus.TryParse("wsl: some warning\r\n{\"docker\":false,\"services\":[]}\r\n", out var s));
        Assert.False(s!.Docker);
        Assert.Empty(s.Services);
        Assert.Equal("", s.ImageTag); // a distro that does not report it
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"running":true,"health":"healthy"}""")] // a distro from before the tray status
    [InlineData("""{"docker":"yes","services":[]}""")]
    [InlineData("""{"docker":true,"services":[{"state":"running"}]}""")]
    public void Rejects_what_is_not_a_status(string text) => Assert.False(StackStatus.TryParse(text, out _));

    [Fact]
    public void Everything_fine()
    {
        var r = StatusReport.Build(true, Stack(Svc("appserver"), Svc("denorm-queue", health: "")), 200);
        Assert.Equal(StatusLevel.Ok, r.Level);
        Assert.Equal("BPP — działa", r.Tooltip);
        Assert.Equal(
            new[] { "Dystrybucja WSL: działa", "Docker: działa", "Usługi: działa 2 z 2", "Strona: odpowiada" },
            r.Lines.Select(l => l.Text));
        Assert.All(r.Lines, l => Assert.Equal(StatusLevel.Ok, l.Level));
    }

    [Theory]
    [InlineData(302)] // redirect to the login page
    [InlineData(404)]
    public void Any_answer_below_500_means_the_site_works(int code)
        => Assert.Equal(StatusLevel.Ok, StatusReport.Build(true, Stack(Svc("appserver")), code).Level);

    [Fact]
    public void Distro_stopped_is_the_cause_shown_in_the_tooltip()
    {
        var r = StatusReport.Build(false, null, null);
        Assert.Equal(StatusLevel.Bad, r.Level);
        Assert.Equal("BPP — Dystrybucja WSL: zatrzymana", r.Tooltip);
        Assert.Equal(
            new[] { "Dystrybucja WSL: zatrzymana", "Docker: —", "Usługi: —", "Strona: nie odpowiada" },
            r.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Distro_stopped_ignores_a_stale_stack_answer()
    {
        var r = StatusReport.Build(false, Stack(Svc("appserver")), null);
        Assert.Equal("Docker: —", r.Lines[1].Text);
        Assert.Equal("Usługi: —", r.Lines[2].Text);
    }

    [Fact]
    public void Docker_down()
    {
        var r = StatusReport.Build(true, new StackStatus(false, Array.Empty<ServiceStatus>()), null);
        Assert.Equal(StatusLevel.Bad, r.Level);
        Assert.Equal("BPP — Docker: nie odpowiada", r.Tooltip);
        Assert.Equal("Usługi: —", r.Lines[2].Text);
    }

    [Fact]
    public void No_answer_from_bpp_ctl()
    {
        var r = StatusReport.Build(true, null, 200);
        Assert.Equal(StatusLevel.Bad, r.Level);
        Assert.StartsWith("BPP — Docker: stan nieznany", r.Tooltip);
    }

    [Fact]
    public void Services_starting_make_a_silent_site_starting_too()
    {
        var r = StatusReport.Build(true, Stack(Svc("dbserver"), Svc("appserver", health: "starting")), 502);
        Assert.Equal(StatusLevel.Starting, r.Level);
        Assert.Equal("Usługi: działa 1 z 2; uruchamianie: appserver", r.Lines[2].Text);
        Assert.Equal(new StatusLine("Strona", "błąd 502", StatusLevel.Starting), r.Lines[3]);
        Assert.Equal("BPP — Usługi: działa 1 z 2; uruchamianie: appserver", r.Tooltip);
    }

    [Fact]
    public void A_service_down_is_bad_even_when_the_site_answers()
    {
        var r = StatusReport.Build(true,
            Stack(Svc("appserver"), Svc("workerserver", "exited", ""), Svc("celerybeat", health: "starting")), 200);
        Assert.Equal(StatusLevel.Bad, r.Level);
        Assert.Equal("Usługi: działa 1 z 3; uruchamianie: celerybeat; nie działa: workerserver", r.Lines[2].Text);
        Assert.Equal(StatusLevel.Ok, r.Lines[3].Level);
    }

    [Fact]
    public void Site_silent_with_all_services_up_is_bad()
    {
        var r = StatusReport.Build(true, Stack(Svc("appserver")), null);
        Assert.Equal(StatusLevel.Bad, r.Level);
        Assert.Equal("BPP — Strona: nie odpowiada", r.Tooltip);
    }

    [Fact]
    public void No_services_at_all_is_bad()
        => Assert.Equal(new StatusLine("Usługi", "brak", StatusLevel.Bad),
            StatusReport.Build(true, Stack(), null).Lines[2]);

    [Fact]
    public void Tooltip_never_exceeds_the_limit_of_a_notification_icon()
    {
        var many = Enumerable.Range(0, 30).Select(i => Svc($"service-number-{i}", "exited", "")).ToArray();
        var r = StatusReport.Build(true, Stack(many), 200);
        Assert.Equal(StatusReport.MaxTooltip, r.Tooltip.Length);
        Assert.EndsWith("…", r.Tooltip);
    }
}
