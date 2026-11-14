using BPP.Core;

namespace BPP.Core.Tests;

public class DemoThemesTests
{
    [Fact]
    public void Preselected_theme_is_always_one_of_the_fictional_worlds_and_both_come_up()
    {
        var picked = Enumerable.Range(0, 200)
            .Select(seed => DemoThemes.All[DemoThemes.PreselectedIndex(new Random(seed))].Key)
            .Distinct().Order().ToArray();
        Assert.Equal(new[] { "lem", "wiedzmin" }, picked);
    }

    [Fact]
    public void Preselected_keys_are_valid_themes() => Assert.All(DemoThemes.PreselectedKeys, k => Assert.True(DemoThemes.IsValid(k)));
}
