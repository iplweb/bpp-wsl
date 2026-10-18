using System.Runtime.Versioning;
using BPP.Core;
using Microsoft.Win32;

namespace BPP.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsFeatures
{
    public const int RestartRequired = 3010;

    /// <summary>
    /// Unelevated heuristic (DISM feature queries need admin): the LxssManager service exists once
    /// Microsoft-Windows-Subsystem-Linux is enabled, vmcompute once VirtualMachinePlatform is.
    /// </summary>
    public bool IsWslFeatureEnabled()
        => ServiceKeyExists("LxssManager") && ServiceKeyExists("vmcompute");

    /// <summary>
    /// One elevated DISM call for both features. Returns 0, 3010 (<see cref="RestartRequired"/>), DISM's failing
    /// exit code, or null when the user refused UAC.
    /// </summary>
    public int? EnableWslFeaturesElevated() => Elevation.Run(ElevatedCommands.EnableWsl());

    private static bool ServiceKeyExists(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        return key != null;
    }
}
