using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using BPP.Core;

namespace BPP.Windows;

/// <summary>Host list for <c>bpp-ctl --hosts</c>: localhost plus this computer's LAN IPv4 addresses.</summary>
[SupportedOSPlatform("windows")]
public static class LocalHosts
{
    public static IReadOnlyList<string> Current()
    {
        var interfaces = new List<NetInterface>();
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var ipv4 = n.GetIPProperties().UnicastAddresses
                    .Select(u => u.Address)
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .ToList();
                interfaces.Add(new NetInterface(n.Name, n.Description, n.OperationalStatus == OperationalStatus.Up,
                    n.NetworkInterfaceType == NetworkInterfaceType.Loopback, ipv4));
            }
            catch (NetworkInformationException)
            {
                // interface vanished while enumerating
            }
        }
        return HostList.From(interfaces);
    }
}
