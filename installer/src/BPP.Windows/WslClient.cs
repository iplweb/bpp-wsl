using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using BPP.Core;

namespace BPP.Windows;

[SupportedOSPlatform("windows")]
public sealed class WslClient
{
    private const string CtlPath = "/opt/bpp/bin/bpp-ctl";

    private static string WslExe => Path.Combine(Environment.SystemDirectory, "wsl.exe");

    /// <summary>Receives bpp-ctl stderr lines and non-JSON stdout lines (for the "Szczegóły" pane).</summary>
    public event Action<string>? LogLine;

    public async Task<bool> IsWslReadyAsync()
    {
        var r = await TryRunWslAsync("--status").ConfigureAwait(false);
        return r is { ExitCode: 0 };
    }

    public Task<bool> DistroExistsAsync() => ListedAsync("-l", "-q");

    /// <summary>
    /// Whether the BPP distro is running, without starting it (<c>wsl -l --running -q</c>; with nothing running
    /// wsl exits non-zero, which also reads as "not running").
    /// </summary>
    public Task<bool> IsDistroRunningAsync() => ListedAsync("-l", "--running", "-q");

    private static async Task<bool> ListedAsync(params string[] args)
    {
        var r = await TryRunWslAsync(args).ConfigureAwait(false);
        if (r is not { ExitCode: 0 }) return false;
        return r.StdOut.Split('\n')
            .Any(l => string.Equals(l.Trim(), AppPaths.DistroName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Null when wsl.exe is missing or cannot be started.</summary>
    private static async Task<ProcessResult?> TryRunWslAsync(params string[] args)
    {
        if (!File.Exists(WslExe)) return null;
        try { return await ProcessRunner.RunAsync(WslExe, args, utf8Env: true).ConfigureAwait(false); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public async Task ImportAsync(string tarPath)
    {
        Directory.CreateDirectory(AppPaths.DistroDir);
        await RunWslAsync("import", "--import", AppPaths.DistroName, AppPaths.DistroDir, tarPath, "--version", "2").ConfigureAwait(false);
    }

    public Task UnregisterAsync() => RunWslAsync("unregister", "--unregister", AppPaths.DistroName);

    public Task TerminateAsync() => RunWslAsync("terminate", "--terminate", AppPaths.DistroName);

    /// <summary>Best effort: sparse VHD is an optimisation, failure is ignored.</summary>
    public async Task TrySetSparseAsync()
    {
        try
        {
            await ProcessRunner.RunAsync(WslExe,
                new[] { "--manage", AppPaths.DistroName, "--set-sparse", "true" }, utf8Env: true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    public Task InstallWslPackageAsync() => RunWslAsync("install", "--install", "--no-distribution");

    /// <summary>
    /// Runs bpp-ctl as root in the BPP distro and streams its JSON events. <paramref name="args"/> is split on
    /// whitespace (bpp-ctl arguments never contain spaces). If bpp-ctl exits non-zero without a final
    /// "error" event, a synthetic error event is emitted.
    /// </summary>
    public async IAsyncEnumerable<BppEvent> RunCtlAsync(
        string args, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var p = StartCtl(args);
        using var reg = ct.Register(() => ProcessRunner.TryKill(p));
        var stderrTask = PumpStderrAsync(p);

        BppEvent? last = null;
        try
        {
            string? line;
            while ((line = await p.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false)) != null)
            {
                line = WslText.CleanLine(line);
                if (BppEventParser.TryParse(line, out var ev) && ev != null)
                {
                    last = ev;
                    yield return ev;
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    LogLine?.Invoke(line);
                }
            }
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            ProcessRunner.TryKill(p);
            try { await stderrTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }

        if (p.ExitCode != 0 && last?.Step != "error")
            yield return new BppEvent("error", null, $"bpp-ctl zakończył się kodem {p.ExitCode}.");
    }

    /// <summary>Runs bpp-ctl and returns stdout (for <c>logs</c> / <c>status</c>). Throws on non-zero exit.</summary>
    public async Task<string> RunCtlTextAsync(string args, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync(WslExe, CtlArgv(args), utf8Env: true, ct).ConfigureAwait(false);
        foreach (var l in r.StdErr.Split('\n'))
            if (l.Length > 0) LogLine?.Invoke(l);

        if (r.ExitCode != 0)
        {
            var msg = r.StdOut.Split('\n')
                .Select(l => BppEventParser.TryParse(l.Trim(), out var ev) ? ev : null)
                .LastOrDefault(e => e?.Step == "error")?.Message;
            throw new InvalidOperationException(msg ?? $"bpp-ctl {args} zakończył się kodem {r.ExitCode}.");
        }
        return r.StdOut;
    }

    private static List<string> CtlArgv(string args)
    {
        var argv = new List<string> { "-d", AppPaths.DistroName, "-u", "root", "--", CtlPath };
        argv.AddRange(args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return argv;
    }

    private static Process StartCtl(string args)
    {
        var psi = ProcessRunner.CreateStartInfo(WslExe, CtlArgv(args), utf8Env: true);
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        return Process.Start(psi) ?? throw new InvalidOperationException("Nie można uruchomić wsl.exe.");
    }

    private async Task PumpStderrAsync(Process p)
    {
        string? line;
        while ((line = await p.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            line = WslText.CleanLine(line);
            if (line.Length > 0) LogLine?.Invoke(line);
        }
    }

    private static async Task RunWslAsync(string what, params string[] args)
    {
        var r = await ProcessRunner.RunAsync(WslExe, args, utf8Env: true).ConfigureAwait(false);
        if (r.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut : r.StdErr;
            throw new InvalidOperationException($"wsl {what} zakończyło się kodem {r.ExitCode}: {detail.Trim()}");
        }
    }
}
