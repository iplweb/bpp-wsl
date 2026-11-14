namespace BPP.Core;

public enum AppMode { Tray, Uninstall, Setup }

/// <summary>
/// Command line of the one BPP.exe (tray and installer in one file):
/// <c>BPP.exe [--tray]</c>, <c>BPP.exe --uninstall</c>,
/// <c>BPP.exe --setup [--resume] [--install-anyway] [--rootfs &lt;file&gt;] [--demo &lt;theme&gt;]</c>.
/// </summary>
/// <param name="Demo">Demo data theme (a <see cref="DemoThemes"/> key) chosen before a restart; null = none.</param>
public sealed record AppArgs(AppMode Mode, bool Resume = false, bool InstallAnyway = false, string? Rootfs = null,
    string? Demo = null)
{
    public const string DemoArg = "--demo";
    public const string Setup = "--setup";
    public const string ResumeArg = "--resume";
    public const string InstallAnywayArg = "--install-anyway";
    public const string RootfsArg = "--rootfs";

    /// <summary>Null for anything else (the exe then exits with code 2 and no UI).</summary>
    public static AppArgs? Parse(IReadOnlyList<string> args)
    {
        switch (args)
        {
            case []:
            case ["--tray"]:
                return new AppArgs(AppMode.Tray);
            case ["--uninstall"]:
                return new AppArgs(AppMode.Uninstall);
        }
        if (args.Count == 0 || args[0] != Setup) return null;

        var result = new AppArgs(AppMode.Setup);
        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case ResumeArg when !result.Resume:
                    result = result with { Resume = true };
                    break;
                case InstallAnywayArg when !result.InstallAnyway:
                    result = result with { InstallAnyway = true };
                    break;
                case RootfsArg when result.Rootfs == null && i + 1 < args.Count && args[i + 1].Length > 0:
                    result = result with { Rootfs = args[++i] };
                    break;
                case DemoArg when result.Demo == null && i + 1 < args.Count && DemoThemes.IsValid(args[i + 1]):
                    result = result with { Demo = args[++i] };
                    break;
                default:
                    return null;
            }
        }
        return result;
    }

    /// <summary>Arguments for the RunOnce entry that resumes Setup after a restart (quoted for a command line).</summary>
    public string ResumeCommandArgs()
    {
        var parts = new List<string> { Setup, ResumeArg };
        if (InstallAnyway) parts.Add(InstallAnywayArg);
        if (Rootfs != null) parts.Add($"{RootfsArg} \"{Rootfs}\"");
        if (Demo != null) parts.Add($"{DemoArg} {Demo}");
        return string.Join(" ", parts);
    }
}
