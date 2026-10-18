using System.Runtime.Versioning;
using Microsoft.Win32;

namespace BPP.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsFeatures
{
    private const int RestartRequired = 3010;
    private static string Dism => Path.Combine(Environment.SystemDirectory, "dism.exe");

    /// <summary>
    /// Unelevated heuristic (DISM feature queries need admin): the LxssManager service exists once
    /// Microsoft-Windows-Subsystem-Linux is enabled, vmcompute once VirtualMachinePlatform is.
    /// </summary>
    public bool IsWslFeatureEnabled()
        => ServiceKeyExists("LxssManager") && ServiceKeyExists("vmcompute");

    /// <summary>Needs admin. Returns 0, 3010 (restart required) or the first failing DISM exit code.</summary>
    public int EnableWslFeatures()
    {
        var restart = false;
        foreach (var feature in new[] { "Microsoft-Windows-Subsystem-Linux", "VirtualMachinePlatform" })
        {
            var r = ProcessRunner.Run(Dism, new[]
            {
                "/online", "/enable-feature", $"/featurename:{feature}", "/all", "/norestart",
            });
            if (r.ExitCode == RestartRequired) restart = true;
            else if (r.ExitCode != 0) return r.ExitCode;
        }
        return restart ? RestartRequired : 0;
    }

    private static bool ServiceKeyExists(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        return key != null;
    }
}
