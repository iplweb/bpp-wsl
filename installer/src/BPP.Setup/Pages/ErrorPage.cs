namespace BPP.Setup.Pages;

/// <summary>Title, problem list, collapsible log, optional "Spróbuj ponownie", "Zamknij".</summary>
internal sealed class ErrorPage : Page
{
    public ErrorPage(string title, IReadOnlyList<string> problems, string log, Action? retry, Action close)
    {
        AddRow(Title(title));
        AddRow(Body(problems.Count == 1 ? problems[0] : "• " + string.Join("\n• ", problems)));

        if (log.Length > 0)
        {
            var box = LogBox();
            box.Text = log;
            AddRow(DetailsToggle(box));
            AddRow(box, fill: true);
        }

        var buttons = new List<Button> { Action("Zamknij", close) };
        if (retry != null) buttons.Add(Action("Spróbuj ponownie", retry));
        AddButtons(buttons.ToArray());
    }
}
