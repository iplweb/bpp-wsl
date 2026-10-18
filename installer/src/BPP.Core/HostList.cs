using System.Net;
using System.Net.Sockets;

namespace BPP.Core;

public static class HostList
{
    private const string Localhost = "localhost";

    public static IReadOnlyList<string> From(IEnumerable<NetInterface> interfaces)
    {
        var ips = interfaces
            .Where(i => i.IsUp && !i.IsLoopback && !IsVirtual(i))
            .SelectMany(i => i.IPv4)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IsLinkLocal(a))
            .Select(a => a.ToString())
            .Distinct()
            .OrderBy(s => s, Comparer<string>.Create(CompareOctets));

        return new[] { Localhost }.Concat(ips).ToList();
    }

    public static string ToCsv(IReadOnlyList<string> hosts) => string.Join(",", hosts);

    private static bool IsVirtual(NetInterface i) =>
        Mentions(i.Name, "WSL") || Mentions(i.Description, "WSL") ||
        Mentions(i.Name, "Hyper-V") || Mentions(i.Description, "Hyper-V");

    private static bool Mentions(string text, string needle) =>
        text.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool IsLinkLocal(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }

    private static int CompareOctets(string? x, string? y)
    {
        var a = IPAddress.Parse(x!).GetAddressBytes();
        var b = IPAddress.Parse(y!).GetAddressBytes();
        return a.AsSpan().SequenceCompareTo(b);
    }
}
