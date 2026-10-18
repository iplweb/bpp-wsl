using BPP.Core;

namespace BPP.Core.Tests;

public class UninstallPlanTests
{
    [Fact]
    public void Distro_gone_removes_everything_and_schedules_program_dir_removal()
        => Assert.Equal(new UninstallCleanup(RemoveData: true, RemoveRegistration: true, RemoveFirewallRule: true,
            RemoveProgramDir: true), UninstallPlan.AfterUnregister(distroGone: true));

    [Fact]
    public void Distro_still_there_removes_nothing_so_the_uninstall_can_be_retried()
        => Assert.Equal(new UninstallCleanup(RemoveData: false, RemoveRegistration: false, RemoveFirewallRule: false,
            RemoveProgramDir: false), UninstallPlan.AfterUnregister(distroGone: false));

    [Theory]
    [InlineData(false, false, false, true)] // nothing to unregister (partial install, or a retry after a late success)
    [InlineData(true, true, false, true)]   // unregistered and no longer listed
    [InlineData(true, true, true, false)]   // wsl said ok but the distro is still listed
    [InlineData(true, false, false, false)] // wsl --unregister failed: not trusted even if the listing is empty
    [InlineData(true, false, true, false)]
    public void Distro_counts_as_gone_only_when_never_there_or_unregistered_and_unlisted(
        bool existedBefore, bool unregisterSucceeded, bool listedAfterwards, bool gone)
        => Assert.Equal(gone, UninstallPlan.DistroGone(existedBefore, unregisterSucceeded, listedAfterwards));

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)] // stopped distro: `bpp-ctl stop` would only boot it, go straight to unregister
    [InlineData(false, false, false)]
    public void Stops_the_stack_only_when_the_distro_is_running(bool exists, bool running, bool stop)
        => Assert.Equal(stop, UninstallPlan.ShouldStopBeforeUnregister(exists, running));
}
