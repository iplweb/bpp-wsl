namespace BPP.Setup.Pages;

/// <summary>
/// Base page: one column — content rows added with <see cref="AddRow"/>, a filler, then a right-aligned button row.
/// </summary>
internal abstract class Page : UserControl
{
    private static readonly Font TitleFont = new((SystemFonts.MessageBoxFont ?? DefaultFont).FontFamily, 13f, FontStyle.Bold);
    private static readonly Font LogFont = new(FontFamily.GenericMonospace, 8.5f);

    private readonly TableLayoutPanel _layout;
    private readonly FlowLayoutPanel _buttons;

    protected Page()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(24, 8, 24, 16);

        _layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
        };
        Controls.Add(_layout);
    }

    /// <summary>Adds a content row; <paramref name="fill"/> rows take the remaining height.</summary>
    protected void AddRow(Control c, bool fill = false)
    {
        c.Dock = DockStyle.Fill;
        _layout.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
        _layout.Controls.Add(c, 0, _layout.RowCount++);
    }

    /// <summary>Adds buttons right to left (the first one is rightmost) and closes the layout.</summary>
    protected void AddButtons(params Button[] buttons)
    {
        if (!_layout.RowStyles.Cast<RowStyle>().Any(r => r.SizeType == SizeType.Percent))
            AddRow(new Panel(), fill: true);
        foreach (var b in buttons) _buttons.Controls.Add(b);
        AddRow(_buttons);
    }

    protected static Label Title(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = TitleFont,
        Margin = new Padding(0, 0, 0, 10),
    };

    protected static Label Body(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(500, 0),
        Margin = new Padding(0, 0, 0, 8),
    };

    /// <summary>
    /// The action is posted, not run inside Click: it usually replaces (and disposes) this page,
    /// which must not happen while the button is still processing its own click.
    /// </summary>
    protected static Button Action(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(100, 30), Padding = new Padding(6, 0, 6, 0) };
        b.Click += (_, _) => b.BeginInvoke(onClick);
        return b;
    }

    /// <summary>Read-only monospace log box (hidden until "Szczegóły" is clicked).</summary>
    protected static TextBox LogBox() => new()
    {
        Multiline = true,
        ReadOnly = true,
        WordWrap = false,
        ScrollBars = ScrollBars.Both,
        Font = LogFont,
        MaxLength = 0,
        Visible = false,
    };

    /// <summary>A "Szczegóły ▸ / ▾" link that shows and hides <paramref name="target"/>.</summary>
    protected static LinkLabel DetailsToggle(Control target)
    {
        var link = new LinkLabel { Text = "Szczegóły ▸", AutoSize = true, Margin = new Padding(0, 6, 0, 4) };
        link.LinkClicked += (_, _) =>
        {
            target.Visible = !target.Visible;
            link.Text = target.Visible ? "Szczegóły ▾" : "Szczegóły ▸";
        };
        return link;
    }
}
