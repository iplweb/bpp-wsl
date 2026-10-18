using System.Runtime.Versioning;
using BPP.Core;

namespace BPP.Windows;

/// <summary>Inbound firewall rule for the app's own port. Changes go through an elevated System32 netsh.exe.</summary>
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

    /// <summary>
    /// Adds the rule for <paramref name="programPath"/> with one UAC prompt, only when it is missing.
    /// Returns 0 on success (or when it already exists), netsh's exit code on failure, null when UAC was refused.
    /// </summary>
    public int? AddElevated(string programPath)
        => RuleExists() ? 0 : Elevation.Run(ElevatedCommands.AddFirewallRule(programPath));

    /// <summary>Removes the rule with one UAC prompt, only when it exists. Same return contract as AddElevated.</summary>
    public int? RemoveElevated()
        => RuleExists() ? Elevation.Run(ElevatedCommands.DeleteFirewallRule()) : 0;
}
