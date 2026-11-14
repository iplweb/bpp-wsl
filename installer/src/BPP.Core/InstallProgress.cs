namespace BPP.Core;

/// <summary>
/// Progress of the whole installation (the "Całość" bar), 0..1. Each step owns a slice of the bar sized by how
/// long it usually takes; image pulls dominate. Steps that only show a final page are 1.
/// </summary>
public static class InstallProgress
{
    private const double DownloadStart = 0.04;
    private const double DownloadEnd = 0.10;
    private const double InitStart = 0.15;
    private const double PullStart = 0.18;
    private const double PullEnd = 0.85;

    /// <summary>Where the bar stands when <paramref name="step"/> starts.</summary>
    public static double AtStart(InstallerStep step) => step switch
    {
        InstallerStep.Preflight or InstallerStep.StopRunningBpp => 0,
        InstallerStep.InstallProgram => 0.01,
        InstallerStep.RegisterUninstall => 0.02,
        InstallerStep.CheckWsl or InstallerStep.EnableWsl or InstallerStep.InstallWslPackage
            or InstallerStep.ScheduleResume => 0.03,
        InstallerStep.DownloadRootfs => DownloadStart,
        InstallerStep.CheckDistro or InstallerStep.AskReinstall or InstallerStep.Unregister => DownloadEnd,
        InstallerStep.ImportDistro => 0.11,
        InstallerStep.RunInit or InstallerStep.StopDistro => InitStart,
        InstallerStep.AddFirewall => 0.96,
        InstallerStep.CreateShortcut => 0.98,
        _ => 1,
    };

    /// <summary>Position while the rootfs downloads (<paramref name="fraction"/> 0..1 of the file).</summary>
    public static double DuringDownload(double fraction)
        => DownloadStart + (DownloadEnd - DownloadStart) * Math.Clamp(fraction, 0, 1);

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

/// <summary>
/// The four stages the progress page lists for the user (under "Instalowanie BPP"); each installer step belongs
/// to one. A stage the machine does not need (WSL already there) is simply passed over.
/// </summary>
public static class InstallStages
{
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "Sprawdzenie komputera i włączenie WSL (Linux w systemie Windows)",
        "Pobranie i instalacja aktualnej wersji WSL z internetu",
        "Pobranie dystrybucji BPP (ok. 170 MB)",
        "Instalacja i uruchomienie BPP (pobranie obrazów, ok. 2 GB)",
    };

    /// <summary>Index into <see cref="Names"/> of the stage <paramref name="step"/> belongs to.</summary>
    public static int Of(InstallerStep step) => step switch
    {
        InstallerStep.InstallWslPackage => 1,
        InstallerStep.DownloadRootfs => 2,
        InstallerStep.Preflight or InstallerStep.StopRunningBpp or InstallerStep.InstallProgram
            or InstallerStep.RegisterUninstall or InstallerStep.CheckWsl or InstallerStep.EnableWsl
            or InstallerStep.ScheduleResume => 0,
        _ => 3,
    };
}
