using System.Text;
using System.Text.RegularExpressions;

namespace BPP.Core;

/// <summary>Parsed <c>--elevated &lt;task&gt; [args]</c> request.</summary>
public sealed record ElevatedRequest(string Task, IReadOnlyList<string> Args);

/// <summary>Builds, parses and quotes the arguments of the UAC-elevated helper invocation.</summary>
public static class ElevatedArgs
{
    public const string Flag = "--elevated";
    public const string EnableWsl = "enable-wsl";
    public const string Firewall = "firewall";
    public const string RemoveFirewall = "remove-firewall";

    private static readonly Regex AbsoluteWindowsPath = new(@"^([A-Za-z]:[\\/]|\\\\)", RegexOptions.Compiled);

    public static string[] Build(string task, params string[] args)
        => new[] { Flag, task }.Concat(args).ToArray();

    /// <summary>Returns null when args are not a well-formed, known elevated request.</summary>
    public static ElevatedRequest? Parse(string[] args)
    {
        if (args.Length < 2 || args[0] != Flag) return null;
        var rest = args.Skip(2).ToArray();
        switch (args[1])
        {
            case EnableWsl:
            case RemoveFirewall:
                return rest.Length == 0 ? new ElevatedRequest(args[1], rest) : null;
            case Firewall:
                return rest.Length == 1 && AbsoluteWindowsPath.IsMatch(rest[0])
                    ? new ElevatedRequest(args[1], rest)
                    : null;
            default:
                return null;
        }
    }

    /// <summary>Joins arguments into one Windows command line (CommandLineToArgvW rules).</summary>
    public static string ToCommandLine(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));

    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;

        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') sb.Append('\\', backslashes * 2 + 1).Append('"');
            else sb.Append('\\', backslashes).Append(c);
            backslashes = 0;
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }
}
