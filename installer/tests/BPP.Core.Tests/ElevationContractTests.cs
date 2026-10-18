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
    }

    [Fact]
    public void Add_firewall_rule_quotes_name_and_program_path_with_spaces()
    {
        var c = ElevatedCommands.AddFirewallRule(@"C:\Users\Jan Kowalski\AppData\Local\Programs\BPP\BPP.exe");
        Assert.Equal("netsh.exe", c.FileName);
        Assert.Equal("advfirewall firewall add rule name=\"BPP (8443)\" dir=in action=allow " +
                     "program=\"C:\\Users\\Jan Kowalski\\AppData\\Local\\Programs\\BPP\\BPP.exe\" " +
                     "protocol=TCP localport=8443 profile=any enable=yes", c.Arguments);
    }

    [Fact]
    public void Add_firewall_rule_path_without_spaces_is_left_bare()
        => Assert.Contains(" program=C:\\BPP\\BPP.exe ", ElevatedCommands.AddFirewallRule(@"C:\BPP\BPP.exe").Arguments);

    [Fact]
    public void Delete_firewall_rule_by_name()
    {
        var c = ElevatedCommands.DeleteFirewallRule();
        Assert.Equal("netsh.exe", c.FileName);
        Assert.Equal("advfirewall firewall delete rule name=\"BPP (8443)\"", c.Arguments);
    }

    [Theory]
    [InlineData("BPP.exe")]
    [InlineData(@"..\BPP.exe")]
    [InlineData(@"C:\a\"" & calc & ""\BPP.exe")]
    [InlineData("")]
    public void Add_firewall_rule_rejects_relative_or_quoted_paths(string path)
        => Assert.Throws<ArgumentException>(() => ElevatedCommands.AddFirewallRule(path));

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
