using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using BPP.Core;

namespace BPP.Windows;

[SupportedOSPlatform("windows")]
public static class Elevation
{
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// Starts <paramref name="exePath"/> with the UAC prompt (verb runas), waits, and returns true on exit code 0.
    /// Returns false when the user declines the prompt. UseShellExecute forbids ArgumentList, so the single
    /// argument string is built with <see cref="ElevatedArgs.ToCommandLine"/>.
    /// </summary>
    public static bool Run(string exePath, params string[] args)
    {
        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = ElevatedArgs.ToCommandLine(args),
        };
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
    }
}
