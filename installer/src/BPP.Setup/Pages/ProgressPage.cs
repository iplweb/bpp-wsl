namespace BPP.Setup.Pages;

/// <summary>Step label, progress bar (marquee when the progress is unknown), collapsible log, "Anuluj".</summary>
internal sealed class ProgressPage : Page
{
    private readonly Label _status;
    private readonly ProgressBar _bar;
    private readonly TextBox _log;
    private readonly Button _cancel;

    public ProgressPage(Action cancel)
    {
        _status = Body("Przygotowywanie…");
        _bar = new ProgressBar { Height = 22, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
        _log = LogBox();
        _cancel = Action("Anuluj", cancel);

        AddRow(Title("Instalowanie BPP"));
        AddRow(_status);
        AddRow(_bar);
        AddRow(DetailsToggle(_log, growBy: 220));
        AddRow(_log, fill: true);
        AddButtons(_cancel);
    }

    /// <summary><paramref name="fraction"/> is bpp-ctl's 0..1 progress; null shows an indeterminate bar.</summary>
    public void SetStatus(string text, double? fraction)
    {
        if (text.Length > 0) _status.Text = text;
        if (fraction is { } f)
        {
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Value = Math.Clamp((int)Math.Round(f * 100), 0, 100);
        }
        else
        {
            _bar.Style = ProgressBarStyle.Marquee;
        }
    }

    public void AppendLog(string line)
    {
        if (!IsDisposed) _log.AppendText(line + Environment.NewLine); // posted lines may arrive while closing
    }
}
