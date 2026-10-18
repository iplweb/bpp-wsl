using BPP.Windows;

namespace BPP.Tray;

/// <summary>One line of the tray's own log (bpp-ctl stderr, tray messages), numbered to avoid duplicates.</summary>
internal sealed record TrayLogLine(long Seq, string Text);

/// <summary>"Pokaż logi": <c>bpp-ctl logs</c> plus the tray's recent lines, with copy and refresh.</summary>
internal sealed class LogsForm : Form
{
    private readonly WslClient _wsl;
    private readonly Func<IReadOnlyList<TrayLogLine>> _trayLog;
    private readonly TextBox _text;
    private readonly Button _refresh;
    private readonly Font _font = new(FontFamily.GenericMonospace, 9f);
    private readonly CancellationTokenSource _closed = new();
    private long _lastSeq;

    public LogsForm(WslClient wsl, Func<IReadOnlyList<TrayLogLine>> trayLog, Icon icon)
    {
        _wsl = wsl;
        _trayLog = trayLog;

        Text = "BPP — logi";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(900, 600);
        MinimumSize = new Size(400, 250);

        _text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = _font,
            MaxLength = 0,
        };

        _refresh = new Button { Text = "Odśwież", AutoSize = true };
        _refresh.Click += async (_, _) => await RefreshLogsAsync();
        var copy = new Button { Text = "Kopiuj do schowka", AutoSize = true };
        copy.Click += (_, _) => CopyToClipboard();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(6),
        };
        buttons.Controls.Add(copy);
        buttons.Controls.Add(_refresh);

        Controls.Add(_text);
        Controls.Add(buttons);

        Shown += async (_, _) => await RefreshLogsAsync();
        FormClosed += (_, _) => _closed.Cancel(); // kills an in-flight `bpp-ctl logs` client
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closed.Dispose();
            _font.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Thread-safe: may be called from any thread; ignored once the window is closed.</summary>
    public void AppendLine(TrayLogLine line)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => AppendLine(line)); }
            catch (InvalidOperationException) { /* handle destroyed while closing */ }
            return;
        }
        if (line.Seq <= _lastSeq) return;
        _lastSeq = line.Seq;
        _text.AppendText(line.Text + Environment.NewLine);
    }

    private async Task RefreshLogsAsync()
    {
        _refresh.Enabled = false;
        _text.Text = "Pobieranie logów…";
        string services;
        try
        {
            services = await _wsl.RunCtlTextAsync("logs", _closed.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            services = "Nie udało się pobrać logów usług: " + ex.Message;
        }
        if (IsDisposed || _closed.IsCancellationRequested) return;

        var tray = _trayLog();
        _lastSeq = tray.Count > 0 ? tray[^1].Seq : _lastSeq;
        var lines = new List<string> { "=== Logi usług (bpp-ctl logs) ===" };
        lines.AddRange(services.TrimEnd().Split('\n'));
        lines.Add("");
        lines.Add("=== Komunikaty ikonki BPP ===");
        lines.AddRange(tray.Select(l => l.Text));
        _text.Text = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        _text.SelectionStart = _text.TextLength;
        _text.ScrollToCaret();
        _refresh.Enabled = true;
    }

    private void CopyToClipboard()
    {
        if (_text.TextLength == 0) return;
        try
        {
            Clipboard.SetText(_text.Text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, "Schowek jest zajęty przez inny program. Spróbuj ponownie.", "BPP",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
