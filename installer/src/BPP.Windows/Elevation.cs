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
    /// Runs <paramref name="command"/> (a System32 program, Ruling R8) through the UAC prompt (verb runas),
    /// waits, and returns its exit code; null when the user declined the prompt or it could not start.
    /// </summary>
    public static int? Run(ElevatedCommand command)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, command.FileName))
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = command.Arguments,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return null;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return null;
        }
    }
}
