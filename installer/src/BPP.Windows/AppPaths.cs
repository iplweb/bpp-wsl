using System.Security.Principal;
using BPP.Core;

namespace BPP.Windows;

public static class AppPaths
{
    public const string DistroName = "BPP";
    public const int Port = ElevatedCommands.Port;

    private static readonly Lazy<string> RuleName = new(() => BPP.Core.FirewallRuleName.ForUser(
        WindowsIdentity.GetCurrent().User?.Value
        ?? throw new InvalidOperationException("Nie można ustalić identyfikatora SID bieżącego użytkownika.")));

    /// <summary>
    /// Per-user firewall rule name, derived from the SID of the user running this (never elevated) process and
    /// passed to the elevated netsh as data, so the admin who approves UAC never changes whose rule it is.
    /// </summary>
    public static string FirewallRuleName => RuleName.Value;

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string ProgramDir => Path.Combine(LocalAppData, "Programs", "BPP");
    public static string TrayExe => Path.Combine(ProgramDir, "BPP.exe");
    public static string DistroDir => Path.Combine(LocalAppData, "BPP", "wsl");
}
