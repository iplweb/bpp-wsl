namespace BPP.Setup.Pages;

/// <summary>An extra button on the error page (e.g. "Zatrzymaj BPP").</summary>
internal sealed record ErrorAction(string Text, Action Run);

/// <summary>
/// Title, problem list, "Pokaż logi" / "Wyślij diagnostykę do IPLweb…" links, optional "Spróbuj ponownie" or other
/// action, "Zamknij".
/// </summary>
internal sealed class ErrorPage : Page
{
    public ErrorPage(string title, IReadOnlyList<string> problems, Action showLogs, Action sendDiagnostics,
        Action? retry, Action close, ErrorAction? extra = null)
    {
        AddRow(Title(title));
        // the problem list takes the free height and scrolls, so long messages never push the buttons off;
        // it is selectable, so the commands in it can be copied
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

        var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 4) };
        links.Controls.Add(Link("Pokaż logi", showLogs));
        links.Controls.Add(Link("Wyślij diagnostykę do IPLweb…", sendDiagnostics));
        AddRow(links);

        var buttons = new List<Button> { Action("Zamknij", close) };
        if (retry != null) buttons.Add(Action("Spróbuj ponownie", retry));
        if (extra != null) buttons.Add(Action(extra.Text, extra.Run));
        AddButtons(buttons.ToArray());
    }

    private static LinkLabel Link(string text, Action onClick)
    {
        var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 0, 18, 0) };
        link.LinkClicked += (_, _) => link.BeginInvoke(onClick);
        return link;
    }
}
