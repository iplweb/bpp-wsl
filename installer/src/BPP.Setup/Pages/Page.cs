namespace BPP.Setup.Pages;

/// <summary>
/// Base page: one column — content rows added with <see cref="AddRow"/>, a filler, then a right-aligned button row.
/// </summary>
internal abstract class Page : UserControl
{
    private static readonly Font TitleFont = new((SystemFonts.MessageBoxFont ?? DefaultFont).FontFamily, 13f, FontStyle.Bold);
    protected static readonly Font BoldFont = new(SystemFonts.MessageBoxFont ?? DefaultFont, FontStyle.Bold);
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

    /// <summary>
    /// Read-only monospace log box (hidden until "Szczegóły" is clicked). Long lines wrap: the wizard window is
    /// narrow, and a horizontal scroll bar would eat a line of an already small box.
    /// </summary>
    protected static TextBox LogBox() => new()
    {
        Multiline = true,
        ReadOnly = true,
        WordWrap = true,
        ScrollBars = ScrollBars.Vertical,
        Font = LogFont,
        MaxLength = 0,
        Visible = false,
    };

    private Control? _details;
    private LinkLabel? _detailsLink;
    private int _detailsGrowBy;
    private bool _detailsShown; // own state: Control.Visible also reflects the parents, so it is not a reliable toggle
    private int _collapsedHeight; // window height before the details were opened; 0 = not opened

    /// <summary>
    /// A "Szczegóły ▸ / ▾" link that shows and hides <paramref name="target"/>; showing it makes the window
    /// <paramref name="growBy"/> (96-DPI units) taller instead of squeezing the page, hiding shrinks it back.
    /// </summary>
    protected LinkLabel DetailsToggle(Control target, int growBy)
    {
        var link = new LinkLabel { Text = "Szczegóły ▸", AutoSize = true, Margin = new Padding(0, 6, 0, 4) };
        _details = target;
        _detailsLink = link;
        _detailsGrowBy = growBy;
        link.LinkClicked += (_, _) => SetDetailsVisible(!_detailsShown);
        return link;
    }

    /// <summary>Called before the page is (re)shown in a window of base height: details start collapsed.</summary>
    public void CollapseDetails()
    {
        if (_details == null || _detailsLink == null) return;
        _details.Visible = false;
        _detailsLink.Text = "Szczegóły ▸";
        _detailsShown = false;
        _collapsedHeight = 0; // the window is already back at its base height
    }

    private void SetDetailsVisible(bool visible)
    {
        if (_details == null || _detailsLink == null || _detailsShown == visible) return;
        _detailsShown = visible;
        _detailsLink.Text = visible ? "Szczegóły ▾" : "Szczegóły ▸";
        var form = FindForm();
        if (form == null)
        {
            _details.Visible = visible;
            return;
        }

        form.SuspendLayout();
        try
        {
            if (visible)
            {
                // remember the exact height to return to, instead of subtracting the same amount later
                _collapsedHeight = form.Height;
                form.Height = _collapsedHeight + LogicalToDeviceUnits(_detailsGrowBy);
                _details.Visible = true;
            }
            else
            {
                _details.Visible = false;
                if (_collapsedHeight > 0) form.Height = _collapsedHeight;
                _collapsedHeight = 0;
            }
        }
        finally
        {
            form.ResumeLayout(performLayout: true);
        }
        var area = Screen.FromControl(form).WorkingArea;
        if (form.Bottom > area.Bottom) form.Top = Math.Max(area.Top, area.Bottom - form.Height);
    }
}
