using System.Net;
using System.Net.Sockets;

namespace BPP.Core;

/// <summary>Addresses the LAN relay listens on: the IPv4 entries of a host list, without <c>localhost</c>.</summary>
public static class RelayAddresses
{
    public static IReadOnlyList<IPAddress> From(IReadOnlyList<string> hosts)
        => hosts
            .Where(h => !string.Equals(h, "localhost", StringComparison.OrdinalIgnoreCase))
            .Select(h => IPAddress.TryParse(h, out var a) ? a : null)
            .OfType<IPAddress>()
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .ToList();
}
