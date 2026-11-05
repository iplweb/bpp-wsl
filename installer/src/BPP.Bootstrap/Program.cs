using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BPP.Core.Download;

namespace BPP.Bootstrap;

/// <summary>
/// The mini <c>BPP-Setup.exe</c>. Finds the real installer (BPP.exe of the same version) next to itself or
/// downloads it from GitHub Releases, verifies the pinned SHA-256 and runs it as <c>--setup</c>. A rootfs archive
/// lying next to this exe is handed over with <c>--rootfs</c> (offline install / networks that block GitHub).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // .NET Framework 4.8 uses the OS TLS defaults; make sure TLS 1.2 is allowed on older configurations.
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        // authenticating proxies (company / university networks): log in as the current Windows user
        if (WebRequest.DefaultWebProxy != null)
            WebRequest.DefaultWebProxy.Credentials = CredentialCache.DefaultNetworkCredentials;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var form = new BootstrapForm();
        Application.Run(form);
        return form.ExitCode;
    }
}

internal static class Pinned
{
    private static string Get(string key)
        => (typeof(Pinned).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == key)?.Value ?? "").Trim();

    public static string AppUrl => Get("AppUrl");
    public static string? AppSha256 => Get("AppSha256") is { Length: > 0 } h ? h : null;
    public static string AppFileName => Get("AppFileName") is { Length: > 0 } n ? n : "BPP.exe";
    public static string RootfsFileName => Get("RootfsFileName") is { Length: > 0 } n ? n : "bpp-rootfs.tar.gz";
}

internal sealed class BootstrapForm : Form
{
    private const string Caption = "Instalator BPP";

    private readonly Label _status;
    private readonly ProgressBar _bar;
    private readonly Button _cancel;
    private readonly CancellationTokenSource _cts = new();
    private long _lastReport;

    public int ExitCode { get; private set; }

    public BootstrapForm()
    {
        Text = Caption;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch (ArgumentException) { }
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(440, 150);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        Font = SystemFonts.MessageBoxFont;

        var title = new Label
        {
            Text = "BPP — Bibliografia Publikacji Pracowników",
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 16),
        };
        _status = new Label
        {
            Text = "Przygotowywanie…",
            AutoEllipsis = true,
            Location = new Point(20, 50),
            Size = new Size(400, 20),
        };
        _bar = new ProgressBar
        {
            Location = new Point(20, 74),
            Size = new Size(400, 20),
            Maximum = 1000,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30,
        };
        _cancel = new Button
        {
            Text = "Anuluj",
            Location = new Point(320, 108),
            Size = new Size(100, 28),
        };
        _cancel.Click += (_, _) => Close();
        Controls.AddRange(new Control[] { title, _status, _bar, _cancel });
        CancelButton = _cancel;
        Shown += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        var here = AppDomain.CurrentDomain.BaseDirectory;
        while (true)
        {
            try
            {
                var (app, downloaded) = await GetAppAsync(here);
                Launch(app, downloaded, Path.Combine(here, Pinned.RootfsFileName));
                return;
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                ExitCode = 1;
                return;
            }
            catch (Exception ex) when (ex is DownloadFailedException or IOException or UnauthorizedAccessException
                                           or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                if (IsDisposed) return;
                var answer = MessageBox.Show(this,
                    "Nie udało się pobrać instalatora BPP.\n\n" + ex.Message + "\n\n" +
                    "Sprawdź połączenie z internetem i spróbuj ponownie. Jeśli sieć blokuje GitHub, pobierz pliki " +
                    $"{Pinned.AppFileName} i {Pinned.RootfsFileName} z zakładki Releases na innym komputerze " +
                    "i połóż je obok BPP-Setup.exe.",
                    Caption, MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning);
                if (answer != DialogResult.Retry)
                {
                    ExitCode = 1;
                    Close();
                    return;
                }
            }
        }
    }

    /// <summary>The installer next to this exe (checked against the pinned hash), else the downloaded copy.</summary>
    private async Task<(string Path, bool Downloaded)> GetAppAsync(string here)
    {
        var sha = Pinned.AppSha256;
        var local = Path.Combine(here, Pinned.AppFileName);
        if (File.Exists(local))
        {
            SetStatus("Sprawdzanie pliku instalatora…", null);
            if (sha == null || await Task.Run(() => FileHash.Matches(local, sha))) return (local, false);
        }

        if (Pinned.AppUrl.Length == 0 || sha == null)
            throw new InvalidOperationException(
                $"Ta kompilacja nie zna adresu instalatora. Połóż plik {Pinned.AppFileName} obok BPP-Setup.exe.");

        var dest = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BPP", "download", Pinned.AppFileName);
        SetStatus("Pobieranie instalatora BPP…", 0);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BPP-Setup/" + Application.ProductVersion.Split('+')[0]);
        var progress = new Progress<DownloadProgress>(Report);
        // off the UI thread: a cached copy is hashed before the first await
        var token = _cts.Token;
        await Task.Run(() => new Downloader(http).DownloadAsync(new Uri(Pinned.AppUrl), dest, sha, progress, token));
        return (dest, true);
    }

    private void Report(DownloadProgress p)
    {
        var now = Environment.TickCount;
        if (unchecked(now - _lastReport) < 200 && p.Fraction != 1.0) return;
        _lastReport = now;
        SetStatus(p.Total is { } t
            ? $"Pobieranie instalatora BPP… {Mb(p.Received)} z {Mb(t)} MB"
            : $"Pobieranie instalatora BPP… {Mb(p.Received)} MB", p.Fraction);
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0");

    private void SetStatus(string text, double? fraction)
    {
        if (IsDisposed) return;
        _status.Text = text;
        if (fraction is { } f)
        {
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Value = (int)Math.Round(Math.Max(0, Math.Min(1, f)) * _bar.Maximum);
        }
        else _bar.Style = ProgressBarStyle.Marquee;
    }

    /// <summary>
    /// Starts the installer and hides this window. A downloaded copy is deleted once the installer exits (it has
    /// copied itself to the program folder by then); this process just waits for that, without a window.
    /// </summary>
    private void Launch(string app, bool downloaded, string rootfs)
    {
        var args = "--setup";
        if (File.Exists(rootfs)) args += $" --rootfs \"{rootfs}\"";
        SetStatus("Uruchamianie instalatora…", 1);
        var process = Process.Start(new ProcessStartInfo(app, args)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(app) ?? "",
        }) ?? throw new InvalidOperationException("Nie udało się uruchomić instalatora.");
        Hide();
        ShowInTaskbar = false;
        _ = Task.Run(() =>
        {
            using (process)
            {
                process.WaitForExit();
                ExitCode = process.ExitCode;
            }
            if (downloaded) DeleteWithRetry(app);
        }).ContinueWith(_ => BeginInvoke((Action)Close), TaskScheduler.Default);
    }

    private static void DeleteWithRetry(string path)
    {
        for (var i = 0; i < 10; i++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Thread.Sleep(500);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts.Cancel(); // a running download stops; its .part file is resumed next time
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}
