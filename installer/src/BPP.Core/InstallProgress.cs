namespace BPP.Core;

/// <summary>
/// Progress of the whole installation (the "Całość" bar), 0..1. Each step owns a slice of the bar sized by how
/// long it usually takes; image pulls dominate. Steps that only show a final page are 1.
/// </summary>
public static class InstallProgress
{
    private const double InitStart = 0.15;
    private const double PullStart = 0.18;
    private const double PullEnd = 0.85;

    /// <summary>Where the bar stands when <paramref name="step"/> starts.</summary>
    public static double AtStart(InstallerStep step) => step switch
    {
        InstallerStep.Preflight or InstallerStep.StopRunningBpp => 0,
        InstallerStep.ExtractTray => 0.01,
        InstallerStep.RegisterUninstall => 0.02,
        InstallerStep.CheckWsl or InstallerStep.EnableWsl or InstallerStep.InstallWslPackage
            or InstallerStep.ScheduleResume => 0.03,
        InstallerStep.CheckDistro or InstallerStep.AskReinstall or InstallerStep.Unregister => 0.05,
        InstallerStep.ImportDistro => 0.06,
        InstallerStep.RunInit or InstallerStep.StopDistro => InitStart,
        InstallerStep.AddFirewall => 0.96,
        InstallerStep.CreateShortcut => 0.98,
        _ => 1,
    };

    /// <summary>Position during <c>bpp-ctl init</c>; <see cref="BppEvent.Progress"/> of "pull" is 0..1 over all images.</summary>
    public static double DuringInit(BppEvent ev) => ev.Step switch
    {
        "config" => InitStart,
        "cert" => 0.17,
        "pull" => PullStart + (PullEnd - PullStart) * Math.Clamp(ev.Progress ?? 0, 0, 1),
        "start" => PullEnd,
        "migrate" => 0.88,
        "ready" => 0.95,
        _ => InitStart,
    };
}
