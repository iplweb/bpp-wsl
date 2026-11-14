namespace BPP.Setup.Pages;

/// <summary>
/// The list of the four installation stages (done ✓, current in bold), step label with the current step's bar (the image being pulled; marquee when unknown), the whole-installation
/// bar, "Pokaż terminal" / "Ukryj terminal" with "Zapisz log do pliku…", the terminal with the log (hidden until
/// asked for; it collects the log either way), "Anuluj".
/// </summary>
internal sealed class ProgressPage : Page
{
    private const int BarMax = 1000;

    private readonly Label _status;
    private readonly ProgressBar _bar;
    private readonly Label _overallLabel;
    private readonly ProgressBar _overall;
    private readonly TextBox _log;
    private readonly Button _toggle;
    private readonly Button _cancel;
    private readonly Action _heightChanged;
    private bool _terminalShown;
    private readonly Label[] _stages;
    private int _stage = -1;

    /// <summary>With the terminal shown: room for about 25 rows on top of what the base window leaves.</summary>
    public override int ExtraHeight => _terminalShown ? 340 : 0;

    /// <param name="heightChanged">Called after <see cref="ExtraHeight"/> changed, so the window can resize.</param>
    public ProgressPage(Action cancel, Action saveLog, Action heightChanged)
    {
        _heightChanged = heightChanged;
        _stages = BPP.Core.InstallStages.Names.Select(_ => Body("")).ToArray();
        foreach (var l in _stages) l.Margin = new Padding(0, 0, 0, 3);
        _stages[^1].Margin = new Padding(0, 0, 0, 12);
        RenderStages();

        _status = Body("Przygotowywanie…");
        _bar = new ProgressBar { Height = 22, Maximum = BarMax, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
        _overallLabel = Body("Cała instalacja: 0%");
        _overallLabel.Margin = new Padding(0, 14, 0, 6);
        _overall = new ProgressBar { Height = 22, Maximum = BarMax, Style = ProgressBarStyle.Continuous };
        _log = Terminal();
        _log.Visible = false;
        _toggle = new Button { Text = ShowTerminalText, AutoSize = true, MinimumSize = new Size(130, 28), Margin = new Padding(0, 0, 14, 0) };
        _toggle.Click += (_, _) => ToggleTerminal();
        _cancel = Action("Anuluj", cancel);

        AddRow(Title("Instalowanie BPP"));
        foreach (var l in _stages) AddRow(l);
        AddRow(_status);
        AddRow(_bar);
        AddRow(_overallLabel);
        AddRow(_overall);
        // no Top/Bottom anchor: the flow panel centres the link on the button's height
        var save = new LinkLabel { Text = "Zapisz log do pliku…", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
        save.LinkClicked += (_, _) => saveLog();
        var tools = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 4) };
        tools.Controls.Add(_toggle);
        tools.Controls.Add(save);
        AddRow(tools);
        AddRow(_log, fill: true);
        AddButtons(_cancel);
    }

    private const string ShowTerminalText = "Pokaż terminal";
    private const string HideTerminalText = "Ukryj terminal";

    private void ToggleTerminal()
    {
        _terminalShown = !_terminalShown;
        _toggle.Text = _terminalShown ? HideTerminalText : ShowTerminalText;
        _log.Visible = _terminalShown;
        _heightChanged();
        if (!_terminalShown) return;
        _log.SelectionStart = _log.TextLength; // lines appended while hidden: show the newest ones
        _log.ScrollToCaret();
    }

    /// <summary>
    /// <paramref name="fraction"/> is the current step's 0..1 progress (during the pull: the current image);
    /// null shows an indeterminate bar.
    /// </summary>
    public void SetStatus(string text, double? fraction)
    {
        if (text.Length > 0) _status.Text = text;
        if (fraction is { } f)
        {
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Value = ToBar(f);
        }
        else
        {
            _bar.Style = ProgressBarStyle.Marquee;
        }
    }

    /// <summary>Whole-installation progress 0..1; never moves back (a retried step keeps the bar where it was).</summary>
    public void SetOverall(double fraction)
    {
        var v = ToBar(fraction);
        if (v <= _overall.Value) return;
        _overall.Value = v;
        _overallLabel.Text = $"Cała instalacja: {v * 100 / BarMax}%";
    }

    /// <summary>Marks stage <paramref name="index"/> as the current one; never moves back (WSL is re-checked after its update).</summary>
    public void SetStage(int index)
    {
        if (index <= _stage || index >= _stages.Length) return;
        _stage = index;
        RenderStages();
    }

    private void RenderStages()
    {
        for (var i = 0; i < _stages.Length; i++)
        {
            var mark = i < _stage ? "✓" : i == _stage ? "▸" : "   ";
            _stages[i].Text = $"{mark} {i + 1}. {BPP.Core.InstallStages.Names[i]}";
            _stages[i].Font = i == _stage ? BoldFont : null; // null: back to the inherited font
            _stages[i].ForeColor = i < _stage ? SystemColors.GrayText : SystemColors.ControlText;
        }
    }

    private static int ToBar(double f) => (int)Math.Round(Math.Clamp(f, 0, 1) * BarMax);

    public void AppendLog(string line)
    {
        if (!IsDisposed) _log.AppendText(line + Environment.NewLine); // posted lines may arrive while closing
    }
}
