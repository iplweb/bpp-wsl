namespace BPP.Core;

/// <summary>
/// Demo data offered by the installer: the <c>--motyw</c> keys of BPP's <c>create_demo_data</c> that we expose
/// and their names in the UI. The same keys are whitelisted in <c>bpp-ctl init --demo</c>.
/// </summary>
public static class DemoThemes
{
    public sealed record Theme(string Key, string Label);

    public static readonly IReadOnlyList<Theme> All = new[]
    {
        new Theme("realistyczny", "Polska uczelnia (dane realistyczne)"),
        new Theme("lem", "Świat Stanisława Lema"),
        new Theme("wiedzmin", "Świat Wiedźmina"),
    };

    /// <summary>The themes one of which the installer preselects at random: the fictional worlds.</summary>
    public static readonly IReadOnlyList<string> PreselectedKeys = new[] { "lem", "wiedzmin" };

    /// <summary>Index in <see cref="All"/> of the theme to preselect: one of <see cref="PreselectedKeys"/>, at random.</summary>
    public static int PreselectedIndex(Random random)
    {
        var key = PreselectedKeys[random.Next(PreselectedKeys.Count)];
        for (var i = 0; i < All.Count; i++)
            if (All[i].Key == key) return i;
        throw new InvalidOperationException($"Motyw danych demo „{key}” nie jest na liście.");
    }

    public static bool IsValid(string? key) => key != null && All.Any(t => t.Key == key);
}
