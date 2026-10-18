namespace BPP.Windows;

public static class AppPaths
{
    public const string DistroName = "BPP";
    public const int Port = 8443;
    public const string FirewallRuleName = "BPP (8443)";

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string ProgramDir => Path.Combine(LocalAppData, "Programs", "BPP");
    public static string TrayExe => Path.Combine(ProgramDir, "BPP.exe");
    public static string DistroDir => Path.Combine(LocalAppData, "BPP", "wsl");
}
