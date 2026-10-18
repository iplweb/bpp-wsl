using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace BPP.Windows;

/// <summary>Per-user (HKCU / current user's Start menu) registration; never needs elevation.</summary>
[SupportedOSPlatform("windows")]
public sealed class Registration
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BPP";
    private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string RunOnceName = "BPP-Setup";

    private static string ShortcutPath
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "BPP.lnk");

    public void RegisterUninstall(string trayExe, string version)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey, writable: true);
        key.SetValue("DisplayName", "BPP (wersja testowa)");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "IPLweb");
        key.SetValue("DisplayIcon", trayExe);
        key.SetValue("UninstallString", $"\"{trayExe}\" --uninstall");
        key.SetValue("InstallLocation", Path.GetDirectoryName(trayExe) ?? "");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public void UnregisterUninstall() => Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

    public void CreateStartMenuShortcut(string trayExe)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell jest niedostępny.");
        Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);

        dynamic? shell = null;
        dynamic? link = null;
        try
        {
            shell = Activator.CreateInstance(type)!;
            link = shell.CreateShortcut(ShortcutPath);
            link.TargetPath = trayExe;
            link.WorkingDirectory = Path.GetDirectoryName(trayExe) ?? "";
            link.IconLocation = trayExe + ",0";
            link.Description = "BPP (wersja testowa)";
            link.Save();
        }
        finally
        {
            if (link != null) Marshal.FinalReleaseComObject(link);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }

    public void RemoveStartMenuShortcut()
    {
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
    }

    public void SetRunOnce(string cmd)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunOnceKey, writable: true);
        key.SetValue(RunOnceName, cmd);
    }

    public void ClearRunOnce()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunOnceKey, writable: true);
        key?.DeleteValue(RunOnceName, throwOnMissingValue: false);
    }
}
