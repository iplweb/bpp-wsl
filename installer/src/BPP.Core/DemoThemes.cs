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

    public static bool IsValid(string? key) => key != null && All.Any(t => t.Key == key);
}
