using BPP.Core;

namespace BPP.Core.Tests;

public class ElevationContractTests
{
    [Fact]
    public void ToCommandLine_quotes_only_where_needed()
        => Assert.Equal("a \"b c\" \"C:\\R&D\\a b\\BPP.exe\"",
            ElevatedArgs.ToCommandLine(new[] { "a", "b c", @"C:\R&D\a b\BPP.exe" }));

    [Fact]
    public void Enable_wsl_is_one_dism_call_for_both_features()
    {
        var c = ElevatedCommands.EnableWsl();
        Assert.Equal("dism.exe", c.FileName);
        Assert.Equal("/online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux " +
                     "/featurename:VirtualMachinePlatform /all /norestart", c.Arguments);
        Assert.True(c.ShowWindow); // the user watches DISM's progress in its console
    }

    [Fact]
    public void Firewall_commands_run_without_a_window()
    {
        Assert.False(ElevatedCommands.AddFirewallRule(Rule, @"C:\Users\a\AppData\Local\BPP\BPP.exe").ShowWindow);
        Assert.False(ElevatedCommands.DeleteFirewallRule(Rule).ShowWindow);
    }

    private const string Rule = "BPP (8443) 72c8a69c";

    // Expected values computed outside this code: printf %s '<sid>' | shasum -a 256 (first 8 hex digits).
    [Theory]
    [InlineData("S-1-5-21-3623811015-3361044348-30300820-1013", "BPP (8443) 72c8a69c")]
    [InlineData("S-1-5-21-1004336348-1177238915-682003330-512", "BPP (8443) efbcf4ba")]
    public void Firewall_rule_name_is_per_user_sha256_of_sid(string sid, string expected)
        => Assert.Equal(expected, FirewallRuleName.ForUser(sid));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Firewall_rule_name_needs_a_sid(string sid)
        => Assert.Throws<ArgumentException>(() => FirewallRuleName.ForUser(sid));

    [Theory]
    [InlineData("BPP (8443)")]
    [InlineData("BPP (8443) 72C8A69C")]
    [InlineData("BPP (8443) 72c8a69")]
    [InlineData("BPP (8443) 72c8a69c\" dir=out")]
    [InlineData("")]
    public void Firewall_builders_reject_names_that_are_not_derived_rule_names(string name)
    {
        Assert.False(FirewallRuleName.IsValid(name));
        Assert.Throws<ArgumentException>(() => ElevatedCommands.AddFirewallRule(name, @"C:\BPP\BPP.exe"));
        Assert.Throws<ArgumentException>(() => ElevatedCommands.DeleteFirewallRule(name));
    }

    [Fact]
    public void Add_firewall_rule_quotes_name_and_program_path_with_spaces()
    {
        var c = ElevatedCommands.AddFirewallRule(Rule, @"C:\Users\Jan Kowalski\AppData\Local\Programs\BPP\BPP.exe");
        Assert.Equal("netsh.exe", c.FileName);
        Assert.Equal("advfirewall firewall add rule name=\"BPP (8443) 72c8a69c\" dir=in action=allow " +
                     "program=\"C:\\Users\\Jan Kowalski\\AppData\\Local\\Programs\\BPP\\BPP.exe\" " +
                     "protocol=TCP localport=8443 profile=any enable=yes", c.Arguments);
    }

    [Fact]
    public void Add_firewall_rule_path_without_spaces_is_left_bare()
        => Assert.Contains(" program=C:\\BPP\\BPP.exe ", ElevatedCommands.AddFirewallRule(Rule, @"C:\BPP\BPP.exe").Arguments);

    [Fact]
    public void Delete_firewall_rule_by_name()
    {
        var c = ElevatedCommands.DeleteFirewallRule(Rule);
        Assert.Equal("netsh.exe", c.FileName);
        Assert.Equal("advfirewall firewall delete rule name=\"BPP (8443) 72c8a69c\"", c.Arguments);
    }

    [Theory]
    [InlineData("BPP.exe")]
    [InlineData(@"..\BPP.exe")]
    [InlineData(@"C:\a\"" & calc & ""\BPP.exe")]
    [InlineData("")]
    public void Add_firewall_rule_rejects_relative_or_quoted_paths(string path)
        => Assert.Throws<ArgumentException>(() => ElevatedCommands.AddFirewallRule(Rule, path));

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData(@"C:\dir\", @"C:\dir\")]
    [InlineData(@"C:\my dir\", "\"C:\\my dir\\\\\"")]
    [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]
    [InlineData("a\tb", "\"a\tb\"")]
    public void Quote_follows_CommandLineToArgvW_rules(string input, string expected)
        => Assert.Equal(expected, ElevatedArgs.Quote(input));
}
