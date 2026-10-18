using BPP.Core;

namespace BPP.Core.Tests;

public class ElevationContractTests
{
    [Fact]
    public void Build_firewall_prefixes_elevated_and_task()
    {
        Assert.Equal(new[] { "--elevated", "firewall", @"C:\a b\BPP.exe" },
            ElevatedArgs.Build("firewall", @"C:\a b\BPP.exe"));
    }

    [Theory]
    [InlineData(@"C:\Users\Jan Kowalski\AppData\Local\Programs\BPP\BPP.exe")]
    [InlineData(@"C:\R&D\a & b\BPP.exe")]
    public void Roundtrip_firewall_path(string path)
    {
        var parsed = ElevatedArgs.Parse(ElevatedArgs.Build("firewall", path));
        Assert.NotNull(parsed);
        Assert.Equal("firewall", parsed!.Task);
        Assert.Equal(new[] { path }, parsed.Args);
    }

    [Fact]
    public void Roundtrip_through_command_line_string()
    {
        var path = @"C:\R&D\a b\BPP.exe";
        var cmd = ElevatedArgs.ToCommandLine(ElevatedArgs.Build("firewall", path));
        Assert.Equal("--elevated firewall \"C:\\R&D\\a b\\BPP.exe\"", cmd);
    }

    [Theory]
    [InlineData("enable-wsl", 0)]
    [InlineData("remove-firewall", 0)]
    public void Parse_tasks_without_args(string task, int argc)
    {
        var parsed = ElevatedArgs.Parse(new[] { "--elevated", task });
        Assert.NotNull(parsed);
        Assert.Equal(task, parsed!.Task);
        Assert.Equal(argc, parsed.Args.Count);
    }

    [Fact] public void Unknown_task_is_null() => Assert.Null(ElevatedArgs.Parse(new[] { "--elevated", "format-c" }));
    [Fact] public void Missing_elevated_flag_is_null() => Assert.Null(ElevatedArgs.Parse(new[] { "firewall", @"C:\x.exe" }));
    [Fact] public void Empty_is_null() => Assert.Null(ElevatedArgs.Parse(Array.Empty<string>()));
    [Fact] public void Firewall_without_path_is_null() => Assert.Null(ElevatedArgs.Parse(new[] { "--elevated", "firewall" }));
    [Fact] public void Firewall_with_relative_path_is_null() => Assert.Null(ElevatedArgs.Parse(new[] { "--elevated", "firewall", "BPP.exe" }));
    [Fact] public void Extra_args_are_null() => Assert.Null(ElevatedArgs.Parse(new[] { "--elevated", "enable-wsl", "x" }));

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
