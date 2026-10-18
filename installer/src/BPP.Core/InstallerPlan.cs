namespace BPP.Core;

/// <summary>What BPP-Setup does next: an action to run, or a final page to show.</summary>
public enum InstallerStep
{
    // actions
    Preflight,
    ExtractTray,
    RegisterUninstall,
    CheckWsl,
    EnableWsl,
    InstallWslPackage,
    ScheduleResume,
    CheckDistro,
    AskReinstall,
    Unregister,
    ImportDistro,
    RunInit,
    StopDistro,
    AddFirewall,
    CreateShortcut,
    StopRunningBpp, // only after "Zatrzymaj BPP" on the PreflightFailed page

    // final pages (keep after every action: IsFinal compares with Done)
    Done,
    RestartRequired,
    PreflightFailed,
    WslNeedsAdmin,
    WslEnableFailed,
    WslUnavailable,
    ImportFailed,
    InitFailed,
    Declined,
    Cancelled,
}

/// <summary>Result of the elevated DISM call (<c>null</c> exit code = UAC refused, 3010 = restart required).</summary>
public enum EnableWslOutcome { NotRun, Ok, RestartRequired, Refused, Failed }

public enum InitOutcome { NotRun, Ready, Failed, Cancelled }

/// <summary>
/// Facts gathered so far. <see cref="Completed"/> is the last action that ran (null before the first one);
/// the flow runs the step <see cref="InstallerPlan.Next"/> returns and records its outcome with <c>with { … }</c>.
/// </summary>
public sealed record InstallerState
{
    public InstallerStep? Completed { get; init; }
    public bool PreflightOk { get; init; }
    public bool WslReady { get; init; }

    /// <summary>Unelevated <c>IsWslFeatureEnabled</c> heuristic: only a hint (Task 10 ruling).</summary>
    public bool WslFeatureHint { get; init; }

    public EnableWslOutcome EnableWsl { get; init; }
    public bool WslPackageInstallAttempted { get; init; }
    public bool DistroExists { get; init; }
    public bool ReinstallConfirmed { get; init; }
    public bool ImportOk { get; init; }
    public InitOutcome Init { get; init; }

    /// <summary>Firewall rule present after <see cref="InstallerStep.AddFirewall"/>; false = BPP stays localhost-only.</summary>
    public bool FirewallOk { get; init; }

    /// <summary>Preflight failed only on port 8443, apparently held by a running BPP distro (<see cref="Preflight.PortHeldByBpp"/>).</summary>
    public bool PortHeldByBpp { get; init; }

    /// <summary>The user clicked "Zatrzymaj BPP" on the PreflightFailed page.</summary>
    public bool StopBppRequested { get; init; }

    /// <summary><see cref="InstallerStep.StopRunningBpp"/> ran (it is offered only once).</summary>
    public bool StopBppAttempted { get; init; }
}

/// <summary>Pure state machine of the installer steps (spec 4.4); the WinForms flow only executes it.</summary>
public static class InstallerPlan
{
    public const int DismRestartRequired = 3010;

    public static InstallerStep Next(InstallerState s) => s.Completed switch
    {
        null => InstallerStep.Preflight,
        InstallerStep.Preflight => s.PreflightOk ? InstallerStep.ExtractTray
            : s.StopBppRequested && !s.StopBppAttempted ? InstallerStep.StopRunningBpp : InstallerStep.PreflightFailed,
        InstallerStep.StopRunningBpp => InstallerStep.Preflight,
        // Right after BPP.exe exists, so a partial or cancelled install can be removed from Settings.
        // ExtractTray and RegisterUninstall deliberately precede AskReinstall: declining a reinstall then leaves
        // the new BPP.exe (and its Apps entry) next to the old distro, which is harmless — the new tray drives the
        // old distro the same way and the uninstaller removes both.
        InstallerStep.ExtractTray => InstallerStep.RegisterUninstall,
        InstallerStep.RegisterUninstall => InstallerStep.CheckWsl,
        InstallerStep.CheckWsl => s.WslReady ? InstallerStep.CheckDistro : WslMissing(s),
        InstallerStep.EnableWsl => s.EnableWsl switch
        {
            EnableWslOutcome.Ok => InstallerStep.InstallWslPackage,
            EnableWslOutcome.RestartRequired => InstallerStep.ScheduleResume,
            EnableWslOutcome.Refused => InstallerStep.WslNeedsAdmin,
            _ => InstallerStep.WslEnableFailed,
        },
        InstallerStep.InstallWslPackage => InstallerStep.CheckWsl,
        InstallerStep.ScheduleResume => InstallerStep.RestartRequired,
        InstallerStep.CheckDistro => s.DistroExists ? InstallerStep.AskReinstall : InstallerStep.ImportDistro,
        InstallerStep.AskReinstall => s.ReinstallConfirmed ? InstallerStep.Unregister : InstallerStep.Declined,
        InstallerStep.Unregister => InstallerStep.ImportDistro,
        // A failed import may mean WSL itself is not usable yet (the feature hint is only a heuristic).
        InstallerStep.ImportDistro => s.ImportOk ? InstallerStep.RunInit
            : s.EnableWsl == EnableWslOutcome.NotRun ? InstallerStep.EnableWsl : InstallerStep.ImportFailed,
        // A failed or cancelled init may leave containers (restart: unless-stopped) holding 8443: stop them.
        InstallerStep.RunInit => s.Init switch
        {
            InitOutcome.NotRun => InstallerStep.RunInit, // after Retry
            InitOutcome.Ready => InstallerStep.AddFirewall,
            _ => InstallerStep.StopDistro,
        },
        InstallerStep.StopDistro => s.Init == InitOutcome.Cancelled ? InstallerStep.Cancelled : InstallerStep.InitFailed,
        InstallerStep.AddFirewall => InstallerStep.CreateShortcut, // refusal only means localhost-only
        InstallerStep.CreateShortcut => InstallerStep.Done,
        var final => throw new InvalidOperationException($"{final} jest stroną końcową, nie krokiem."),
    };

    /// <summary>
    /// WSL not usable: try the package as the ordinary user when the features look enabled, otherwise (or when
    /// that did not help) one elevated DISM call. Once DISM has run, a still-missing WSL is final.
    /// </summary>
    private static InstallerStep WslMissing(InstallerState s)
    {
        if (s.EnableWsl != EnableWslOutcome.NotRun) return InstallerStep.WslUnavailable;
        return s.WslFeatureHint && !s.WslPackageInstallAttempted ? InstallerStep.InstallWslPackage : InstallerStep.EnableWsl;
    }

    /// <summary>True for steps that are final pages (shown, not executed).</summary>
    public static bool IsFinal(InstallerStep step) => step >= InstallerStep.Done;

    /// <summary>Only a failed <c>bpp-ctl init</c> can be retried (init is idempotent on the Linux side).</summary>
    public static bool CanRetry(InstallerStep step) => step == InstallerStep.InitFailed;

    /// <summary>State that makes <see cref="Next"/> run <see cref="InstallerStep.RunInit"/> again.</summary>
    public static InstallerState Retry(InstallerState s)
    {
        if (!CanRetry(Next(s)))
            throw new InvalidOperationException("Ponowić można tylko nieudany krok konfiguracji (bpp-ctl init).");
        return s with { Init = InitOutcome.NotRun, Completed = InstallerStep.RunInit };
    }

    /// <summary>The PreflightFailed page offers "Zatrzymaj BPP": port held by a running BPP distro, not tried yet.</summary>
    public static bool CanStopBpp(InstallerState s)
        => Next(s) == InstallerStep.PreflightFailed && s.PortHeldByBpp && !s.StopBppAttempted;

    /// <summary>State that makes <see cref="Next"/> stop the running BPP distro and then re-run preflight.</summary>
    public static InstallerState StopBpp(InstallerState s)
    {
        if (!CanStopBpp(s))
            throw new InvalidOperationException("Zatrzymać BPP można tylko, gdy port 8443 zajmuje działające BPP.");
        return s with { StopBppRequested = true };
    }

    public static EnableWslOutcome ClassifyEnableWsl(int? exitCode) => exitCode switch
    {
        null => EnableWslOutcome.Refused,
        0 => EnableWslOutcome.Ok,
        DismRestartRequired => EnableWslOutcome.RestartRequired,
        _ => EnableWslOutcome.Failed,
    };
}
