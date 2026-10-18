using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using BPP.Core;
using Microsoft.Win32;

namespace BPP.Windows;

/// <summary>
/// Builds the diagnostics ZIP for IPLweb support: Windows/WSL facts, the app's own log and <c>bpp-ctl diag</c>
/// (container state, healthcheck history, service logs). Every text goes through <see cref="SupportMail.Redact"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Diagnostics
{
    private static readonly TimeSpan DiagTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Writes <c>BPP-diagnostyka-…zip</c> to the Desktop (Documents, then %TEMP% as fallbacks) and returns its path.
    /// <paramref name="appLogName"/> names the app log entry ("instalator", "ikonka");
    /// <paramref name="earlierBppDiag"/> is a report taken before the stack was stopped (installer, failed init).
    /// With <paramref name="stopDistroIfStarted"/>, a distro that diag had to boot is terminated again.
    /// </summary>
    public static async Task<string> CreateZipAsync(string appLogName, string appLog, WslClient wsl,
        string? earlierBppDiag, bool stopDistroIfStarted, CancellationToken ct = default)
    {
        var windows = await WindowsInfoAsync().ConfigureAwait(false);
        var bpp = await BppDiagAsync(wsl, stopDistroIfStarted, ct).ConfigureAwait(false);

        var path = Path.Combine(TargetDir(), SupportMail.ZipFileName(DateTime.Now));
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Add(zip, "windows.txt", windows);
            Add(zip, appLogName + "-log.txt", appLog);
            Add(zip, "bpp-diag.txt", bpp);
            if (earlierBppDiag != null) Add(zip, "bpp-diag-przed-zatrzymaniem.txt", earlierBppDiag);
        }
        return path;
    }

    /// <summary>
    /// <c>bpp-ctl diag</c> output, or a line saying why it is missing; never throws except on cancellation.
    /// </summary>
    public static async Task<string> BppDiagAsync(WslClient wsl, bool stopDistroIfStarted, CancellationToken ct = default)
    {
        if (!await wsl.DistroExistsAsync().ConfigureAwait(false)) return "Dystrybucja BPP nie jest zainstalowana.";
        var wasRunning = await wsl.IsDistroRunningAsync().ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(DiagTimeout);
        try
        {
            return await wsl.RunCtlTextAsync("diag", timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return $"bpp-ctl diag nie zakończył się w ciągu {DiagTimeout.TotalMinutes:0} min.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return "bpp-ctl diag: " + ex.Message;
        }
        finally
        {
            if (stopDistroIfStarted && !wasRunning)
            {
                try { await wsl.TerminateAsync().ConfigureAwait(false); }
                catch (InvalidOperationException) { }
            }
        }
    }

    private static string TargetDir()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments })
        {
            var dir = Environment.GetFolderPath(folder);
            if (dir.Length > 0 && Directory.Exists(dir)) return dir;
        }
        return Path.GetTempPath();
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        w.Write(SupportMail.Redact(text));
    }

    private static async Task<string> WindowsInfoAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Program: {Environment.ProcessPath} {typeof(Diagnostics).Assembly.GetName().Version}");
        sb.AppendLine($"Windows: {WindowsVersion()} ({Environment.OSVersion.VersionString}, {RuntimeInformation.OSArchitecture})");
        sb.AppendLine($"Procesory logiczne: {Environment.ProcessorCount}");
        sb.AppendLine($"RAM: {InstalledRamGb()}");
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(AppPaths.DistroDir)!);
            sb.AppendLine($"Wolne miejsce na {drive.Name}: {drive.AvailableFreeSpace / (double)(1L << 30):0.0} GB");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            sb.AppendLine("Wolne miejsce: " + ex.Message);
        }
        sb.AppendLine($"Port {AppPaths.Port} wolny: {new Ports().IsPortFree(AppPaths.Port)}");

        foreach (var args in new[] { new[] { "--version" }, new[] { "--status" }, new[] { "-l", "-v" } })
        {
            sb.AppendLine();
            sb.AppendLine($"===== wsl {string.Join(' ', args)} =====");
            sb.AppendLine(await WslTextAsync(args).ConfigureAwait(false));
        }

        var wslconfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig");
        sb.AppendLine();
        sb.AppendLine("===== .wslconfig =====");
        try { sb.AppendLine(File.Exists(wslconfig) ? File.ReadAllText(wslconfig) : "(brak)"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { sb.AppendLine(ex.Message); }
        return sb.ToString();
    }

    private static async Task<string> WslTextAsync(string[] args)
    {
        var wsl = Path.Combine(Environment.SystemDirectory, "wsl.exe");
        if (!File.Exists(wsl)) return "(brak wsl.exe)";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var r = await ProcessRunner.RunAsync(wsl, args, utf8Env: true, timeout.Token).ConfigureAwait(false);
            return $"{r.StdOut.Trim()}\n{r.StdErr.Trim()}\n(kod {r.ExitCode})".Trim();
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException
                                       or System.ComponentModel.Win32Exception)
        {
            return ex.Message;
        }
    }

    private static string WindowsVersion()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key == null) return "?";
        return $"{key.GetValue("ProductName")} {key.GetValue("DisplayVersion")} build {key.GetValue("CurrentBuild")}.{key.GetValue("UBR")}";
    }

    private static string InstalledRamGb()
        => GetPhysicallyInstalledSystemMemory(out var kb) ? $"{kb / (1024.0 * 1024):0.0} GB" : "?";

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
}
