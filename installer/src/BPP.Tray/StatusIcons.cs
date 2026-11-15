using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BPP.Core;

namespace BPP.Tray;

/// <summary>
/// The tray icon in three variants, with a green, amber or red dot in its corner, and the same dots alone
/// for the status lines of the menu.
/// </summary>
internal sealed class StatusIcons : IDisposable
{
    private static readonly StatusLevel[] Levels = { StatusLevel.Ok, StatusLevel.Starting, StatusLevel.Bad };

    private readonly Dictionary<StatusLevel, Icon> _icons = new();
    private readonly Dictionary<StatusLevel, Bitmap> _dots = new();
    private readonly List<IntPtr> _handles = new();

    public StatusIcons(Icon baseIcon)
    {
        foreach (var level in Levels)
        {
            _icons[level] = WithDot(baseIcon, Colour(level));
            _dots[level] = Dot(Colour(level));
        }
    }

    public Icon Icon(StatusLevel level) => _icons[level];

    public Image Dot(StatusLevel level) => _dots[level];

    private static Color Colour(StatusLevel level) => level switch
    {
        StatusLevel.Ok => Color.FromArgb(0x2E, 0xA0, 0x43),
        StatusLevel.Starting => Color.FromArgb(0xE3, 0xA0, 0x08),
        _ => Color.FromArgb(0xD9, 0x30, 0x25),
    };

    private Icon WithDot(Icon baseIcon, Color colour)
    {
        var size = baseIcon.Size;
        using var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawIcon(baseIcon, new Rectangle(Point.Empty, size));
            // bottom right corner, a little over half of the icon; a white rim keeps it readable on the icon
            var d = size.Width * 9 / 16f;
            var rim = Math.Max(1f, size.Width / 16f);
            var box = new RectangleF(size.Width - d, size.Height - d, d, d);
            using var white = new SolidBrush(Color.White);
            using var fill = new SolidBrush(colour);
            g.FillEllipse(white, box);
            box.Inflate(-rim, -rim);
            g.FillEllipse(fill, box);
        }
        // an icon made from a handle does not own it: the handle is destroyed in Dispose
        var handle = bmp.GetHicon();
        _handles.Add(handle);
        return System.Drawing.Icon.FromHandle(handle);
    }

    private static Bitmap Dot(Color colour)
    {
        var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(colour);
        g.FillEllipse(fill, 3, 3, 10, 10);
        return bmp;
    }

    public void Dispose()
    {
        foreach (var icon in _icons.Values) icon.Dispose();
        foreach (var dot in _dots.Values) dot.Dispose();
        foreach (var handle in _handles) DestroyIcon(handle);
        _icons.Clear();
        _dots.Clear();
        _handles.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
