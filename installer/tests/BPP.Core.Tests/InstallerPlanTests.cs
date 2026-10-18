using BPP.Core;
using S = BPP.Core.InstallerStep;

namespace BPP.Core.Tests;

public class InstallerPlanTests
{
    private static readonly InstallerState Fresh = new();

    private static InstallerState After(S completed, InstallerState? s = null)
        => (s ?? Fresh) with { Completed = completed };

    // ---- 1. preflight, 2. tray ----

    [Fact] public void Starts_with_preflight() => Assert.Equal(S.Preflight, InstallerPlan.Next(Fresh));

    [Fact]
    public void Preflight_problems_stop_the_install()
        => Assert.Equal(S.PreflightFailed, InstallerPlan.Next(After(S.Preflight) with { PreflightOk = false }));

    [Fact]
    public void Preflight_ok_extracts_tray_before_wsl()
        => Assert.Equal(S.ExtractTray, InstallerPlan.Next(After(S.Preflight) with { PreflightOk = true }));

    [Fact]
    public void Tray_then_uninstall_entry_so_partial_installs_are_removable()
        => Assert.Equal(S.RegisterUninstall, InstallerPlan.Next(After(S.ExtractTray)));

    [Fact]
    public void Uninstall_entry_then_wsl_check()
        => Assert.Equal(S.CheckWsl, InstallerPlan.Next(After(S.RegisterUninstall)));

    // ---- 1a. port 8443 held by a running BPP distro (no tray) ----

    private static InstallerState HeldByBpp => After(S.Preflight) with { PreflightOk = false, PortHeldByBpp = true };

    [Fact]
    public void Port_held_by_bpp_shows_preflight_page_offering_stop()
    {
        Assert.Equal(S.PreflightFailed, InstallerPlan.Next(HeldByBpp));
        Assert.True(InstallerPlan.CanStopBpp(HeldByBpp));
    }

    [Fact]
    public void Stop_bpp_is_not_offered_for_other_preflight_failures()
        => Assert.False(InstallerPlan.CanStopBpp(After(S.Preflight) with { PreflightOk = false }));

    [Fact]
    public void Stop_bpp_stops_then_reruns_preflight()
    {
        var s = InstallerPlan.StopBpp(HeldByBpp);
        Assert.Equal(S.StopRunningBpp, InstallerPlan.Next(s));
        Assert.False(InstallerPlan.IsFinal(S.StopRunningBpp));
        s = s with { Completed = S.StopRunningBpp, StopBppAttempted = true };
        Assert.Equal(S.Preflight, InstallerPlan.Next(s));
        Assert.Equal(S.ExtractTray, InstallerPlan.Next(s with { Completed = S.Preflight, PreflightOk = true }));
    }

    [Fact]
    public void Stop_bpp_is_offered_once_then_the_failure_is_final()
    {
        var s = InstallerPlan.StopBpp(HeldByBpp) with { Completed = S.StopRunningBpp, StopBppAttempted = true };
        s = s with { Completed = S.Preflight, PreflightOk = false, PortHeldByBpp = true };
        Assert.Equal(S.PreflightFailed, InstallerPlan.Next(s));
        Assert.False(InstallerPlan.CanStopBpp(s));
        Assert.Throws<InvalidOperationException>(() => InstallerPlan.StopBpp(s));
    }

    [Fact]
    public void Stop_bpp_outside_the_preflight_page_is_rejected()
        => Assert.Throws<InvalidOperationException>(() => InstallerPlan.StopBpp(After(S.StopDistro) with
        {
            Init = InitOutcome.Failed, PortHeldByBpp = true,
        }));

    // ---- 3. WSL ----

    [Fact]
    public void Wsl_ready_goes_to_distro_check()
        => Assert.Equal(S.CheckDistro, InstallerPlan.Next(After(S.CheckWsl) with { WslReady = true }));

    [Fact]
    public void Wsl_missing_and_features_off_enables_wsl()
        => Assert.Equal(S.EnableWsl, InstallerPlan.Next(After(S.CheckWsl) with { WslFeatureHint = false }));

    [Fact]
    public void Wsl_missing_but_features_on_installs_package_without_uac()
        => Assert.Equal(S.InstallWslPackage, InstallerPlan.Next(After(S.CheckWsl) with { WslFeatureHint = true }));

    [Fact]
    public void Wsl_still_missing_after_package_offers_enable_wsl_despite_hint()
        => Assert.Equal(S.EnableWsl, InstallerPlan.Next(After(S.CheckWsl) with
        {
            WslFeatureHint = true, WslPackageInstallAttempted = true,
        }));

    [Fact]
    public void Package_install_is_followed_by_a_recheck()
        => Assert.Equal(S.CheckWsl, InstallerPlan.Next(After(S.InstallWslPackage) with { WslPackageInstallAttempted = true }));

    [Fact]
    public void Enable_wsl_refused_asks_for_admin()
        => Assert.Equal(S.WslNeedsAdmin, InstallerPlan.Next(After(S.EnableWsl) with { EnableWsl = EnableWslOutcome.Refused }));

    [Fact]
    public void Enable_wsl_3010_schedules_resume_then_restart_page()
    {
        var s = After(S.EnableWsl) with { EnableWsl = EnableWslOutcome.RestartRequired };
        Assert.Equal(S.ScheduleResume, InstallerPlan.Next(s));
        Assert.Equal(S.RestartRequired, InstallerPlan.Next(s with { Completed = S.ScheduleResume }));
    }

    [Fact]
    public void Enable_wsl_failure_code_is_an_error()
        => Assert.Equal(S.WslEnableFailed, InstallerPlan.Next(After(S.EnableWsl) with { EnableWsl = EnableWslOutcome.Failed }));

    [Fact]
    public void Enable_wsl_ok_installs_package()
        => Assert.Equal(S.InstallWslPackage, InstallerPlan.Next(After(S.EnableWsl) with { EnableWsl = EnableWslOutcome.Ok }));

    [Fact]
    public void Wsl_still_missing_after_enable_and_package_is_an_error_not_a_loop()
        => Assert.Equal(S.WslUnavailable, InstallerPlan.Next(After(S.CheckWsl) with
        {
            EnableWsl = EnableWslOutcome.Ok, WslPackageInstallAttempted = true, WslFeatureHint = true,
        }));

    [Theory]
    [InlineData(null, EnableWslOutcome.Refused)]
    [InlineData(0, EnableWslOutcome.Ok)]
    [InlineData(3010, EnableWslOutcome.RestartRequired)]
    [InlineData(1, EnableWslOutcome.Failed)]
    [InlineData(740, EnableWslOutcome.Failed)]
    public void Dism_exit_codes(int? code, EnableWslOutcome expected)
        => Assert.Equal(expected, InstallerPlan.ClassifyEnableWsl(code));

    [Fact]
    public void Full_walk_with_features_off_reaches_distro_check()
    {
        var s = Fresh;
        var seen = new List<S>();
        for (var i = 0; i < 20; i++)
        {
            var step = InstallerPlan.Next(s);
            seen.Add(step);
            s = step switch
            {
                S.Preflight => s with { Completed = step, PreflightOk = true },
                S.ExtractTray or S.RegisterUninstall => s with { Completed = step },
                S.CheckWsl => s with { Completed = step, WslReady = s.EnableWsl == EnableWslOutcome.Ok && s.WslPackageInstallAttempted },
                S.EnableWsl => s with { Completed = step, EnableWsl = EnableWslOutcome.Ok },
                S.InstallWslPackage => s with { Completed = step, WslPackageInstallAttempted = true },
                _ => s,
            };
            if (step == S.CheckDistro) break;
        }
        Assert.Equal(new[]
        {
            S.Preflight, S.ExtractTray, S.RegisterUninstall, S.CheckWsl, S.EnableWsl, S.InstallWslPackage, S.CheckWsl, S.CheckDistro,
        }, seen);
    }

    // ---- --resume ----

    [Fact]
    public void Resume_after_restart_reenters_at_wsl_and_installs_package()
    {
        // a new process after reboot: fresh state; the plan is the same, only the welcome page is skipped
        Assert.Equal(S.Preflight, InstallerPlan.Next(Fresh));
        var atWsl = After(S.CheckWsl) with { PreflightOk = true, WslFeatureHint = true };
        Assert.Equal(S.InstallWslPackage, InstallerPlan.Next(atWsl));
    }

    // ---- 4. existing distro ----

    [Fact]
    public void Existing_distro_asks_before_reinstall()
        => Assert.Equal(S.AskReinstall, InstallerPlan.Next(After(S.CheckDistro) with { DistroExists = true }));

    [Fact]
    public void Reinstall_yes_unregisters_then_imports()
    {
        var s = After(S.AskReinstall) with { DistroExists = true, ReinstallConfirmed = true };
        Assert.Equal(S.Unregister, InstallerPlan.Next(s));
        Assert.Equal(S.ImportDistro, InstallerPlan.Next(s with { Completed = S.Unregister }));
    }

    [Fact]
    public void Reinstall_no_aborts_without_touching_the_distro()
        => Assert.Equal(S.Declined, InstallerPlan.Next(After(S.AskReinstall) with { DistroExists = true }));

    [Fact]
    public void No_distro_imports()
        => Assert.Equal(S.ImportDistro, InstallerPlan.Next(After(S.CheckDistro) with { DistroExists = false }));

    // ---- 5. import ----

    [Fact]
    public void Import_ok_runs_init()
        => Assert.Equal(S.RunInit, InstallerPlan.Next(After(S.ImportDistro) with { ImportOk = true }));

    [Fact]
    public void Import_failure_first_updates_wsl()
        => Assert.Equal(S.InstallWslPackage, InstallerPlan.Next(After(S.ImportDistro) with { ImportOk = false, WslReady = true }));

    [Fact]
    public void Import_failure_after_wsl_update_offers_enable_wsl_once()
        => Assert.Equal(S.EnableWsl, InstallerPlan.Next(After(S.ImportDistro) with
        {
            ImportOk = false, WslReady = true, WslPackageInstallAttempted = true,
        }));

    [Fact]
    public void Import_failure_after_update_and_enable_wsl_is_an_error()
        => Assert.Equal(S.ImportFailed, InstallerPlan.Next(After(S.ImportDistro) with
        {
            ImportOk = false, EnableWsl = EnableWslOutcome.Ok, WslPackageInstallAttempted = true,
        }));

    [Fact]
    public void Wsl_that_needs_update_after_restart_ends_installed_not_in_a_loop()
    {
        // after DISM + restart: "wsl --status" passes, but import fails until "wsl --update" ran
        var s = new InstallerState();
        var imports = 0;
        var steps = new List<S>();
        for (var i = 0; i < 40; i++)
        {
            var step = InstallerPlan.Next(s);
            steps.Add(step);
            if (InstallerPlan.IsFinal(step)) break;
            s = step switch
            {
                S.Preflight => s with { Completed = step, PreflightOk = true },
                S.CheckWsl => s with { Completed = step, WslReady = true, WslFeatureHint = true },
                S.CheckDistro => s with { Completed = step, DistroExists = false },
                S.InstallWslPackage => s with { Completed = step, WslPackageInstallAttempted = true },
                S.ImportDistro => s with { Completed = step, ImportOk = ++imports > 1 && s.WslPackageInstallAttempted },
                S.RunInit => s with { Completed = step, Init = InitOutcome.Ready },
                S.AddFirewall => s with { Completed = step, FirewallOk = true },
                _ => s with { Completed = step },
            };
        }
        Assert.Equal(S.Done, steps[^1]);
        Assert.DoesNotContain(S.EnableWsl, steps);
        Assert.Equal(2, imports);
    }

    // ---- 6. init ----

    [Fact]
    public void Init_ready_adds_firewall_rule()
        => Assert.Equal(S.AddFirewall, InstallerPlan.Next(After(S.RunInit) with { Init = InitOutcome.Ready }));

    [Fact]
    public void Init_error_stops_the_distro_then_is_a_retryable_error()
    {
        var s = After(S.RunInit) with { ImportOk = true, Init = InitOutcome.Failed };
        Assert.Equal(S.StopDistro, InstallerPlan.Next(s));
        Assert.Equal(S.InitFailed, InstallerPlan.Next(s with { Completed = S.StopDistro }));
        Assert.True(InstallerPlan.CanRetry(S.InitFailed));
    }

    [Fact]
    public void Retry_reruns_init_only()
    {
        var s = After(S.StopDistro) with { ImportOk = true, Init = InitOutcome.Failed };
        var retried = InstallerPlan.Retry(s);
        Assert.Equal(InitOutcome.NotRun, retried.Init);
        Assert.Equal(S.RunInit, InstallerPlan.Next(retried));
    }

    [Theory]
    [InlineData(S.PreflightFailed)]
    [InlineData(S.WslNeedsAdmin)]
    [InlineData(S.WslEnableFailed)]
    [InlineData(S.WslUnavailable)]
    [InlineData(S.ImportFailed)]
    [InlineData(S.Declined)]
    [InlineData(S.Cancelled)]
    public void Other_errors_are_not_retryable(S step) => Assert.False(InstallerPlan.CanRetry(step));

    [Fact]
    public void Retry_outside_init_failure_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => InstallerPlan.Retry(After(S.ImportDistro) with { ImportOk = false }));
        Assert.Throws<InvalidOperationException>(() => InstallerPlan.Retry(After(S.StopDistro) with { Init = InitOutcome.Cancelled }));
    }

    [Fact]
    public void Cancel_during_init_stops_the_distro_then_ends()
    {
        var s = After(S.RunInit) with { Init = InitOutcome.Cancelled };
        Assert.Equal(S.StopDistro, InstallerPlan.Next(s));
        Assert.Equal(S.Cancelled, InstallerPlan.Next(s with { Completed = S.StopDistro }));
    }

    // ---- 7. firewall, 8. registration ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Firewall_refusal_still_creates_shortcut(bool firewallOk)
        => Assert.Equal(S.CreateShortcut, InstallerPlan.Next(After(S.AddFirewall) with
        {
            Init = InitOutcome.Ready, FirewallOk = firewallOk,
        }));

    [Fact] public void Shortcut_then_done() => Assert.Equal(S.Done, InstallerPlan.Next(After(S.CreateShortcut)));

    [Theory]
    [InlineData(S.Done)]
    [InlineData(S.RestartRequired)]
    [InlineData(S.InitFailed)]
    public void Final_pages_are_not_actions(S step)
    {
        Assert.True(InstallerPlan.IsFinal(step));
        Assert.Throws<InvalidOperationException>(() => InstallerPlan.Next(After(step)));
    }

    [Theory]
    [InlineData(S.Preflight)]
    [InlineData(S.RunInit)]
    [InlineData(S.RegisterUninstall)]
    [InlineData(S.CreateShortcut)]
    public void Actions_are_not_final(S step) => Assert.False(InstallerPlan.IsFinal(step));
}
