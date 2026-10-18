using System.Text.RegularExpressions;

namespace BPP.Core;

/// <summary>A System32 program (file name relative to System32) and its argument string, run with UAC.</summary>
public sealed record ElevatedCommand(string FileName, string Arguments);

/// <summary>
/// The only commands ever elevated (Ruling R8): Microsoft-signed System32 binaries, never BPP.exe, which lives
/// in the user-writable %LOCALAPPDATA% and could be swapped by user-level code before an admin approves UAC.
/// </summary>
public static class ElevatedCommands
{
    public const int Port = 8443;

    private static readonly Regex AbsoluteWindowsPath = new(@"^([A-Za-z]:\\|\\\\)", RegexOptions.Compiled);

    /// <summary>One DISM call (one UAC prompt) for both features. Exit 0 ok, 3010 restart required.</summary>
    public static ElevatedCommand EnableWsl() => new("dism.exe", ElevatedArgs.ToCommandLine(new[]
    {
        "/online", "/enable-feature",
        "/featurename:Microsoft-Windows-Subsystem-Linux", "/featurename:VirtualMachinePlatform",
        "/all", "/norestart",
    }));

    /// <summary>
    /// Inbound allow rule <paramref name="ruleName"/> (see <see cref="FirewallRuleName"/>) for
    /// <paramref name="programPath"/> (absolute, no quotes), TCP 8443, all profiles.
    /// </summary>
    public static ElevatedCommand AddFirewallRule(string ruleName, string programPath)
    {
        RequireRuleName(ruleName);
        if (!AbsoluteWindowsPath.IsMatch(programPath) || programPath.Contains('"') || programPath.Contains(".."))
            throw new ArgumentException($"Wymagana bezwzględna ścieżka programu: '{programPath}'.", nameof(programPath));

        return new("netsh.exe", string.Join(' ',
            "advfirewall firewall add rule", Kv("name", ruleName), "dir=in action=allow",
            Kv("program", programPath), $"protocol=TCP localport={Port} profile=any enable=yes"));
    }

    public static ElevatedCommand DeleteFirewallRule(string ruleName)
    {
        RequireRuleName(ruleName);
        return new("netsh.exe", "advfirewall firewall delete rule " + Kv("name", ruleName));
    }

    private static void RequireRuleName(string ruleName)
    {
        if (!FirewallRuleName.IsValid(ruleName))
            throw new ArgumentException($"Nieprawidłowa nazwa reguły zapory: '{ruleName}'.", nameof(ruleName));
    }

    /// <summary><c>key=value</c> with the value quoted when needed: <c>name="BPP (8443) 1a2b3c4d"</c>.</summary>
    private static string Kv(string key, string value) => key + "=" + ElevatedArgs.Quote(value);
}
