using System.Runtime.Versioning;

namespace BPP.Windows;

/// <summary>Inbound firewall rule for the app's own port. Add/Remove need admin (only called with --elevated).</summary>
[SupportedOSPlatform("windows")]
public sealed class Firewall
{
    private static string Netsh => Path.Combine(Environment.SystemDirectory, "netsh.exe");

    /// <summary>Works unelevated; exit code 0 means the rule exists.</summary>
    public bool RuleExists()
        => ProcessRunner.Run(Netsh, new[]
        {
            "advfirewall", "firewall", "show", "rule", $"name={AppPaths.FirewallRuleName}",
        }).ExitCode == 0;

    /// <summary>Idempotent: removes any existing rule with the same name first.</summary>
    public void Add(string programPath)
    {
        Remove();
        var r = ProcessRunner.Run(Netsh, new[]
        {
            "advfirewall", "firewall", "add", "rule",
            $"name={AppPaths.FirewallRuleName}", "dir=in", "action=allow",
            $"program={programPath}", "protocol=TCP", $"localport={AppPaths.Port}",
            "profile=any", "enable=yes",
        });
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"netsh add rule: kod {r.ExitCode}: {r.StdOut.Trim()}");
    }

    public void Remove()
    {
        if (!RuleExists()) return;
        var r = ProcessRunner.Run(Netsh, new[]
        {
            "advfirewall", "firewall", "delete", "rule", $"name={AppPaths.FirewallRuleName}",
        });
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"netsh delete rule: kod {r.ExitCode}: {r.StdOut.Trim()}");
    }
}
