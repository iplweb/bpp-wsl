using System.Runtime.Versioning;

namespace BPP.Windows;

/// <summary>A large, resizable read-only log window with "Kopiuj do schowka" and an optional diagnostics button.</summary>
[SupportedOSPlatform("windows")]
public sealed class LogViewerForm : Form
{
    private readonly Font _font = new(FontFamily.GenericMonospace, 9f);

    public LogViewerForm(string title, string text, Icon? icon, Action<LogViewerForm>? sendDiagnostics)
    {
        Text = title;
        if (icon != null) Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(960, 640);
        MinimumSize = new Size(420, 260);

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = _font,
            MaxLength = 0,
            Text = text,
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(6),
        };
        var close = new Button { Text = "Zamknij", AutoSize = true };
        close.Click += (_, _) => Close();
        var copy = new Button { Text = "Kopiuj do schowka", AutoSize = true };
        copy.Click += (_, _) => CopyToClipboard(this, box.Text);
        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);
        if (sendDiagnostics != null)
        {
            var send = new Button { Text = "Wyślij diagnostykę do IPLweb…", AutoSize = true };
            send.Click += (_, _) => sendDiagnostics(this);
            buttons.Controls.Add(send);
        }

        Controls.Add(box);
        Controls.Add(buttons);
        Shown += (_, _) =>
        {
            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
        };
    }

    public static void CopyToClipboard(IWin32Window owner, string text)
    {
        if (text.Length == 0) return;
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(owner, "Schowek jest zajęty przez inny program. Spróbuj ponownie.", "BPP",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _font.Dispose();
        base.Dispose(disposing);
    }
}
