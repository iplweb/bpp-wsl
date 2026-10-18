using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;

namespace BPP.Windows;

[SupportedOSPlatform("windows")]
public sealed class Ports
{
    public bool IsPortFree(int port)
        => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().All(e => e.Port != port);

    /// <summary>Name of the process listening on <paramref name="port"/>, or null if unknown.</summary>
    public string? OwnerProcessName(int port)
    {
        try
        {
            var netstat = Path.Combine(Environment.SystemDirectory, "netstat.exe");
            var r = ProcessRunner.Run(netstat, new[] { "-ano", "-p", "TCP" });
            var suffix = ":" + port;
            foreach (var raw in r.StdOut.Split('\n'))
            {
                var cols = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                // Proto, Local address, Foreign address, State, PID
                if (cols.Length >= 5 && cols[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase)
                    && cols[1].EndsWith(suffix, StringComparison.Ordinal)
                    && int.TryParse(cols[4], out var pid))
                {
                    using var proc = Process.GetProcessById(pid);
                    return proc.ProcessName;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
        }
        return null;
    }
}
