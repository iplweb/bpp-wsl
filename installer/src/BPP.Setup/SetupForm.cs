using System.Diagnostics;
using System.Text;
using BPP.Core;
using BPP.Setup.Pages;
using BPP.Windows;

namespace BPP.Setup;

/// <summary>The wizard window: logo on top, one page below. Decisions live in InstallerPlan, steps in InstallerFlow.</summary>
internal sealed class SetupForm : Form
{
    public const string BppUrl = "https://localhost:8443/";
    private const string Caption = "Instalator BPP";
    private const string NoPayload = "Brak danych instalacyjnych w pliku — użyj pełnego instalatora.";

    private readonly bool _resume;
    private readonly Icon _icon;
    private readonly Image _logo;
    private readonly Panel _content;
    private readonly ProgressPage _progress;
    private readonly object _logLock = new();
    private readonly StringBuilder _log = new();
    private InstallerFlow? _flow;
    private bool _closeWhenFinished;
    private bool _forceClose;
    private int _baseHeight;
    private bool _launchTray;

    public SetupForm(bool resume)
    {
        _resume = resume;
        var asm = typeof(SetupForm).Assembly;
        using (var s = asm.GetManifestResourceStream("BPP.Setup.bpp.ico")
                       ?? throw new InvalidOperationException("Brak zasobu bpp.ico."))
            _icon = new Icon(s);
        using (var s = asm.GetManifestResourceStream("BPP.Setup.logo.png")
                       ?? throw new InvalidOperationException("Brak zasobu logo.png."))
        using (var img = Image.FromStream(s))
            _logo = new Bitmap(img); // Image.FromStream needs its stream alive; the copy does not

        Text = Caption;
        Icon = _icon;
        // Layout is in 96-DPI units; WinForms scales the window and pages at 125–150 % DPI.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        _content = new Panel { Dock = DockStyle.Fill };
        var logo = new PictureBox
        {
            Image = _logo,
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Top,
            Height = 96,
            Padding = new Padding(0, 12, 0, 4),
        };
        Controls.Add(_content);
        Controls.Add(logo);

        _progress = new ProgressPage(RequestCancel);
        Shown += OnShown;
    }

    private void OnShown(object? sender, EventArgs e)
    {
        var exe = Environment.ProcessPath;
        var payload = exe == null ? null : TryReadPayload(exe);
        if (exe == null || payload == null)
        {
            ShowError("Nie można zainstalować BPP", new[] { NoPayload }, canRetry: false);
            return;
        }

        _flow = new InstallerFlow(this, exe, payload);
        if (_resume) Install();
        else ShowPage(new WelcomePage(Install));
    }

    private static IReadOnlyDictionary<string, PayloadEntry>? TryReadPayload(string exe)
    {
        try
        {
            var index = PayloadArchive.ReadIndex(exe);
            return index.ContainsKey(InstallerFlow.RootfsEntry) && index.ContainsKey(InstallerFlow.TrayEntry) ? index : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Install() => RunFlow(f => f.RunAsync());

    private void Retry() => RunFlow(f => f.RetryAsync());

    private void StopBpp() => RunFlow(f => f.StopBppAsync());

    /// <summary>The flow never throws (it shows the Error page); closes the window if that was requested meanwhile.</summary>
    private async void RunFlow(Func<InstallerFlow, Task> run)
    {
        if (_flow == null || _flow.IsRunning) return;
        await run(_flow);
        if (_closeWhenFinished) Close();
    }

    // ---- called by InstallerFlow (UI thread, except AppendLog) ----------------------------------------

    public void ShowProgress() => ShowPage(_progress);

    public void SetStatus(string text, double? fraction) => _progress.SetStatus(text, fraction);

    public void SetOverall(double fraction) => _progress.SetOverall(fraction);

    /// <summary>Thread-safe: WslClient raises LogLine from its stdout and stderr pumps concurrently.</summary>
    public void AppendLog(string line)
    {
        var stamped = $"{DateTime.Now:HH:mm:ss} {line}";
        lock (_logLock) _log.AppendLine(stamped);
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => _progress.AppendLog(stamped)); }
        catch (InvalidOperationException) { /* handle destroyed while closing */ }
    }

    /// <param name="canStopBpp">Offer "Zatrzymaj BPP" (port 8443 held by a running BPP distro).</param>
    public void ShowError(string title, IReadOnlyList<string> problems, bool canRetry, bool canStopBpp = false)
    {
        string log;
        lock (_logLock) log = _log.ToString();
        ShowPage(new ErrorPage(title, problems, log, canRetry ? Retry : null, Close,
            canStopBpp ? new ErrorAction("Zatrzymaj BPP", StopBpp) : null));
    }

    public void ShowDone(bool lanEnabled)
    {
        _launchTray = true;
        ShowPage(new DonePage(lanEnabled, OpenBpp, Close));
    }

    public void ShowRestart() => ShowPage(new RestartPage(RestartComputer, Close));

    public bool AskReinstall()
        => MessageBox.Show(this, "BPP jest już zainstalowane.\n\nZainstalować ponownie? Dane testowe zostaną usunięte.",
               Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
           == DialogResult.Yes;

    // ---- pages / window -------------------------------------------------------------------------------

    /// <summary>Replaces the current page; the progress page is kept (its log survives a retry).</summary>
    private void ShowPage(Control page)
    {
        // a page left with "Szczegóły" open made the window taller: every page starts at the base height
        if (_baseHeight == 0) _baseHeight = Height;
        else Height = _baseHeight;
        (page as Pages.Page)?.CollapseDetails();
        var old = _content.Controls.Cast<Control>().ToList();
        _content.Controls.Clear();
        _content.Controls.Add(page);
        foreach (var c in old)
            if (!ReferenceEquals(c, _progress) && !ReferenceEquals(c, page)) c.Dispose();
    }

    /// <summary>"Anuluj" behaves like closing the window (see <see cref="OnFormClosing"/>).</summary>
    private void RequestCancel() => Close();

    private bool ConfirmCancel()
        => MessageBox.Show(this, "Przerwać instalację BPP?\n\nMożesz ją dokończyć później, uruchamiając instalator ponownie.",
               Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    /// <summary>
    /// The user can always close the window. During init a confirmed close first cancels it (the flow then stops
    /// the distro, so nothing keeps holding 8443) and closes when that ends; a second close does not wait. Any
    /// other step is abandoned at once: a rerun of Setup picks up whatever it left (a partial distro is offered
    /// for reinstall, a running DISM or wsl --update finishes in its own console window).
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_forceClose && _flow is { IsRunning: true } && e.CloseReason != CloseReason.WindowsShutDown)
        {
            e.Cancel = true;
            if (_closeWhenFinished) ForceClose(); // already stopping after a cancel: the user does not want to wait
            else if (ConfirmCancel())
            {
                if (_flow.InitRunning)
                {
                    _closeWhenFinished = true;
                    _flow.CancelInit();
                    SetStatus("Przerywanie — zatrzymywanie BPP… (zamknij okno ponownie, aby nie czekać)", null);
                }
                else ForceClose();
            }
        }
        base.OnFormClosing(e);
    }

    private void ForceClose()
    {
        _forceClose = true;
        BeginInvoke(Close);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_launchTray) LaunchTray();
        base.OnFormClosed(e);
    }

    private void OpenBpp()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(BppUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"Nie udało się otworzyć przeglądarki. Otwórz ręcznie adres {BppUrl}", Caption,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void LaunchTray()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(AppPaths.TrayExe)
            {
                UseShellExecute = false,
                WorkingDirectory = AppPaths.ProgramDir,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show("Nie udało się uruchomić BPP. Uruchom je z Menu Start (skrót „BPP”).", Caption,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RestartComputer()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"))
            {
                Arguments = "/r /t 0",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            Close();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, "Nie udało się uruchomić ponownie komputera. Zrób to z menu Start.", Caption,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _progress.Dispose(); // may be detached from _content
            _logo.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
