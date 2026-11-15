namespace BPP.Setup.Pages;

/// <summary>
/// Test-installation notice, the BPP MIT licence (selectable text, with buttons to save and print it) and its
/// acceptance ("Zainstaluj" needs the checkbox; touching the disabled button makes the checkbox blink), and the
/// optional demo data with its theme (on by default, with Lem's or the Witcher's world picked at random), and
/// the choice to start BPP at every sign-in (on by default, with a note about the memory it takes).
/// </summary>
internal sealed class WelcomePage : Page
{
    /// <summary>Keeps the licence text readable now that the demo data rows sit below it.</summary>
    public override int ExtraHeight => 222;

    private static readonly Color BlinkColour = Color.FromArgb(0xFF, 0xD5, 0x4F);
    private const int BlinkHalfPeriods = 6; // three flashes
    private readonly System.Windows.Forms.Timer _blinkTimer = new() { Interval = 160 };
    private int _blinkLeft;

    /// <param name="installerVersion">Version of this installer, e.g. 0.3.3.</param>
    /// <param name="imageTag">Version of BPP it installs (the image tag), empty in development builds.</param>
    public WelcomePage(Action<string?, bool> install, string licence, string installerVersion, string imageTag)
    {
        AddRow(Title("Zainstaluj BPP (wersja do testów)"));
        AddRow(Body(imageTag.Length > 0
            ? $"Instalator w wersji {installerVersion} instaluje BPP w wersji {imageTag}."
            : $"Instalator w wersji {installerVersion} (kompilacja deweloperska)."));
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
            // white, not the grey of a read-only box: it reads as text that can be selected and copied
            BackColor = SystemColors.Window,
            ForeColor = SystemColors.WindowText,
        }, fill: true);

        var licenceButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var save = Action("Zapisz licencję…", () => LicenceText.Save(FindForm(), licence));
        var print = Action("Wydrukuj licencję…", () => LicenceText.Print(FindForm(), licence));
        foreach (var b in new[] { save, print })
        {
            b.MinimumSize = new Size(0, 26);
            b.Margin = new Padding(0, 2, 6, 0);
            licenceButtons.Controls.Add(b);
        }
        AddRow(licenceButtons);

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

        var autostart = new CheckBox
        {
            Text = "Uruchamiaj BPP po zalogowaniu do Windows",
            Checked = true,
            AutoSize = true,
            MaximumSize = new Size(Width80, 0),
            Margin = new Padding(0, 4, 0, 4),
        };
        AddRow(autostart);
        AddRow(Body("BPP działa wtedy w tle, zajmuje kilka GB pamięci RAM i może okresowo spowalniać komputer. " +
                    "Ustawienie można wyłączyć w menu ikonki BPP obok zegara, a samo BPP zatrzymać pozycją " +
                    "„Zatrzymaj”."));

        var installButton = Action("Zainstaluj",
            () => install(demo.Checked ? BPP.Core.DemoThemes.All[theme.SelectedIndex].Key : null, autostart.Checked));
        installButton.Enabled = false;
        accept.CheckedChanged += (_, _) => installButton.Enabled = accept.Checked;
        AddButtons(installButton);

        // "Zainstaluj" is disabled until the licence is accepted: say why when the user reaches for it
        _blinkTimer.Tick += (_, _) =>
        {
            if (--_blinkLeft <= 0 || accept.Checked)
            {
                _blinkTimer.Stop();
                accept.ResetBackColor();
                return;
            }
            if (_blinkLeft % 2 == 1) accept.BackColor = BlinkColour;
            else accept.ResetBackColor();
        };
        accept.CheckedChanged += (_, _) =>
        {
            if (!accept.Checked) return;
            _blinkTimer.Stop();
            accept.ResetBackColor();
        };
        WhenDisabledButtonTouched(installButton, () =>
        {
            _blinkLeft = BlinkHalfPeriods;
            accept.BackColor = BlinkColour;
            _blinkTimer.Start();
        });
        Disposed += (_, _) => _blinkTimer.Dispose();
    }
}
