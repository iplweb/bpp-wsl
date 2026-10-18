using System.Net;

namespace BPP.Core;

public record NetInterface(string Name, string Description, bool IsUp, bool IsLoopback, IReadOnlyList<IPAddress> IPv4);
