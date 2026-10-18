namespace BPP.Setup.Pages;

/// <summary>Test-installation notice, the BPP MIT licence and its acceptance; "Zainstaluj" needs the checkbox.</summary>
internal sealed class WelcomePage : Page
{
    public WelcomePage(Action install, string licence)
    {
        AddRow(Title("Zainstaluj BPP (wersja do testów)"));
        AddRow(Body("Instalacja do wypróbowania BPP, nie do pracy produkcyjnej: dane nie mają kopii zapasowych " +
                    "i są usuwane przy odinstalowaniu oraz instalacji nowszej wersji. Instalator pobierze ok. 2 GB."));
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
            MaximumSize = new Size(500, 0),
            Margin = new Padding(0, 8, 0, 4),
        };
        AddRow(accept);
        var installButton = Action("Zainstaluj", install);
        installButton.Enabled = false;
        accept.CheckedChanged += (_, _) => installButton.Enabled = accept.Checked;
        AddButtons(installButton);
    }
}
