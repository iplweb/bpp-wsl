namespace BPP.Setup.Pages;

/// <summary>
/// Test-installation notice, the BPP MIT licence and its acceptance ("Zainstaluj" needs the checkbox), and the
/// optional demo data with its theme (on by default, with Lem's or the Witcher's world picked at random).
/// </summary>
internal sealed class WelcomePage : Page
{
    /// <summary>Keeps the licence text readable now that the demo data rows sit below it.</summary>
    public override int ExtraHeight => 90;

    public WelcomePage(Action<string?> install, string licence)
    {
        AddRow(Title("Zainstaluj BPP (wersja do testów)"));
        AddRow(Body("Instalacja do wypróbowania BPP, nie do pracy produkcyjnej: dane nie mają kopii zapasowych " +
                    "i są usuwane przy odinstalowaniu oraz instalacji nowszej wersji. Instalator pobierze ok. 800 MB."));
        AddRow(Body("BPP jest udostępniane na licencji MIT, „tak jak jest”, bez jakiejkolwiek gwarancji. Autor ani " +
                    "posiadacz praw autorskich nie ponoszą odpowiedzialności za szkody ani inne skutki użycia:"));
        AddRow(new TextBox
        {
            Text = licence.Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            TabStop = false,
        }, fill: true);

        var accept = new CheckBox
        {
            Text = "Akceptuję warunki licencji MIT, w tym brak gwarancji i wyłączenie odpowiedzialności autora",
            AutoSize = true,
            MaximumSize = new Size(Width80, 0),
            Margin = new Padding(0, 8, 0, 4),
        };
        AddRow(accept);

        // optional demo data: BPP's own generator, run once at the first start; on unless the user unticks it
        var demo = new CheckBox
        {
            Text = "Wypełnij BPP przykładowymi danymi:",
            Checked = true,
            AutoSize = true,
            Margin = new Padding(0, 4, 6, 0),
        };
        var theme = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 260,
            Margin = new Padding(0, 1, 0, 0),
        };
        foreach (var t in BPP.Core.DemoThemes.All) theme.Items.Add(t.Label);
        theme.SelectedIndex = BPP.Core.DemoThemes.PreselectedIndex(Random.Shared);
        demo.CheckedChanged += (_, _) => theme.Enabled = demo.Checked;
        var demoRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        demoRow.Controls.Add(demo);
        demoRow.Controls.Add(theme);
        AddRow(demoRow);
        AddRow(Body("Przykładowe dane to ok. 200 autorów i 1500 publikacji w fikcyjnej uczelni — do obejrzenia " +
                    "BPP bez wpisywania własnych. Bez nich BPP startuje puste."));

        var installButton = Action("Zainstaluj",
            () => install(demo.Checked ? BPP.Core.DemoThemes.All[theme.SelectedIndex].Key : null));
        installButton.Enabled = false;
        accept.CheckedChanged += (_, _) => installButton.Enabled = accept.Checked;
        AddButtons(installButton);
    }
}
