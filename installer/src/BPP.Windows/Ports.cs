using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BPP.Windows;

[SupportedOSPlatform("windows")]
public sealed class Ports
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidListener = 3;
    private const int ErrorInsufficientBuffer = 122;

    public bool IsPortFree(int port)
        => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().All(e => e.Port != port);

    /// <summary>Name of the process listening on <paramref name="port"/> (IPv4 or IPv6), or null if unknown.</summary>
    public string? OwnerProcessName(int port)
    {
        try
        {
            var pid = ListenerPid(AfInet, port) ?? ListenerPid(AfInet6, port);
            if (pid == null) return null;
            using var proc = Process.GetProcessById(pid.Value);
            return proc.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid (6 x DWORD).
    // MIB_TCP6ROW_OWNER_PID: localAddr[16], scopeId, localPort, remoteAddr[16], remoteScope, remotePort, state, pid.
    private static int? ListenerPid(int family, int port)
    {
        var size = 0;
        var rc = GetExtendedTcpTable(IntPtr.Zero, ref size, true, family, TcpTableOwnerPidListener, 0);
        if (rc != ErrorInsufficientBuffer && rc != 0) return null;

        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, true, family, TcpTableOwnerPidListener, 0) != 0) return null;

            var rowSize = family == AfInet ? 24 : 56;
            var portOffset = family == AfInet ? 8 : 20;
            var pidOffset = family == AfInet ? 20 : 52;
            var count = Marshal.ReadInt32(buf);
            for (var i = 0; i < count; i++)
            {
                var row = buf + 4 + i * rowSize;
                var raw = Marshal.ReadInt32(row, portOffset);
                var localPort = ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF); // network byte order
                if (localPort == port) return Marshal.ReadInt32(row, pidOffset);
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, [MarshalAs(UnmanagedType.Bool)] bool bOrder,
        int ulAf, int tableClass, int reserved);
}
