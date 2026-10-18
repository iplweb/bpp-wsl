namespace BPP.Setup.Pages;

/// <summary>
/// Step label with the current step's bar (the image being pulled; marquee when unknown), the whole-installation
/// bar, collapsible log, "Anuluj".
/// </summary>
internal sealed class ProgressPage : Page
{
    private const int BarMax = 1000;

    private readonly Label _status;
    private readonly ProgressBar _bar;
    private readonly Label _overallLabel;
    private readonly ProgressBar _overall;
    private readonly TextBox _log;
    private readonly Button _cancel;

    public ProgressPage(Action cancel)
    {
        _status = Body("Przygotowywanie…");
        _bar = new ProgressBar { Height = 22, Maximum = BarMax, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
        _overallLabel = Body("Cała instalacja: 0%");
        _overallLabel.Margin = new Padding(0, 14, 0, 6);
        _overall = new ProgressBar { Height = 22, Maximum = BarMax, Style = ProgressBarStyle.Continuous };
        _log = LogBox();
        _cancel = Action("Anuluj", cancel);

        AddRow(Title("Instalowanie BPP"));
        AddRow(_status);
        AddRow(_bar);
        AddRow(_overallLabel);
        AddRow(_overall);
        AddRow(DetailsToggle(_log, growBy: 220));
        AddRow(_log, fill: true);
        AddButtons(_cancel);
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

    private static int ToBar(double f) => (int)Math.Round(Math.Clamp(f, 0, 1) * BarMax);

    public void AppendLog(string line)
    {
        if (!IsDisposed) _log.AppendText(line + Environment.NewLine); // posted lines may arrive while closing
    }
}
