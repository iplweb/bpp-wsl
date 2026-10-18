namespace BPP.Setup.Pages;

/// <summary>An extra button on the error page (e.g. "Zatrzymaj BPP").</summary>
internal sealed record ErrorAction(string Text, Action Run);

/// <summary>Title, problem list, collapsible log, optional "Spróbuj ponownie" or other action, "Zamknij".</summary>
internal sealed class ErrorPage : Page
{
    public ErrorPage(string title, IReadOnlyList<string> problems, string log, Action? retry, Action close,
        ErrorAction? extra = null)
    {
        AddRow(Title(title));
        // the problem list takes the free height and scrolls, so long messages never push the buttons off
        AddRow(new TextBox
        {
            Text = problems.Count == 1 ? problems[0] : "• " + string.Join(Environment.NewLine + "• ", problems),
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Control,
            TabStop = false,
        }, fill: true);

        if (log.Length > 0)
        {
            var box = LogBox();
            box.Text = log;
            box.Visible = true;
            box.Dock = DockStyle.Fill;
            // fixed-height holder in an auto-size row: collapses while hidden, never grows with the log
            var holder = new Panel { Height = 110, Visible = false };
            holder.Controls.Add(box);
            AddRow(DetailsToggle(holder, growBy: holder.Height + 6));
            AddRow(holder);
        }

        var buttons = new List<Button> { Action("Zamknij", close) };
        if (retry != null) buttons.Add(Action("Spróbuj ponownie", retry));
        if (extra != null) buttons.Add(Action(extra.Text, extra.Run));
        AddButtons(buttons.ToArray());
    }
}
