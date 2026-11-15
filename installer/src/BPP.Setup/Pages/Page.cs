namespace BPP.Setup.Pages;

/// <summary>
/// Base page: one column — content rows added with <see cref="AddRow"/>, a filler, then a right-aligned button row.
/// </summary>
internal abstract class Page : UserControl
{
    private static readonly Font TitleFont = new((SystemFonts.MessageBoxFont ?? DefaultFont).FontFamily, 13f, FontStyle.Bold);
    protected static readonly Font BoldFont = new(SystemFonts.MessageBoxFont ?? DefaultFont, FontStyle.Bold);
    private static readonly Font TerminalFont = new("Consolas", 9f); // falls back to the default font if missing

    /// <summary>Content width (96-DPI units): 80 terminal columns of Consolas 9 pt plus the scroll bar.</summary>
    protected const int Width80 = 560;

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
        MaximumSize = new Size(Width80, 0),
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

    /// <summary>
    /// Runs <paramref name="action"/> when the mouse comes over <paramref name="button"/> or clicks it while it is
    /// disabled. A disabled button gets no mouse events itself: Windows hands them to its parent, the button row.
    /// </summary>
    protected void WhenDisabledButtonTouched(Button button, Action action)
    {
        var over = false;
        bool Hit(Point p) => !button.Enabled && button.Visible && button.Bounds.Contains(p);
        _buttons.MouseMove += (_, e) =>
        {
            var now = Hit(e.Location);
            if (now && !over) action();
            over = now;
        };
        _buttons.MouseLeave += (_, _) => over = false;
        _buttons.MouseDown += (_, e) =>
        {
            if (Hit(e.Location)) action();
        };
    }

    /// <summary>How much taller than the base window this page wants to be (96-DPI units); the form clamps it to the screen.</summary>
    public virtual int ExtraHeight => 0;

    /// <summary>
    /// The "terminal": white monospace text on black, 80 columns wide, showing what the installer and the services
    /// print. Long lines wrap; it scrolls vertically.
    /// </summary>
    protected static TextBox Terminal() => new()
    {
        Multiline = true,
        ReadOnly = true,
        WordWrap = true,
        ScrollBars = ScrollBars.Vertical,
        Font = TerminalFont,
        BackColor = Color.Black,
        ForeColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        MaxLength = 0,
        TabStop = false,
        Margin = new Padding(0, 4, 0, 4),
    };
}
