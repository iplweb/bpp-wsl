using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using BPP.Core;
using Microsoft.Win32;

namespace BPP.Windows;

[SupportedOSPlatform("windows")]
public sealed class WslClient
{
    private const string CtlPath = "/opt/bpp/bin/bpp-ctl";

    private const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

    private static string WslExe => Path.Combine(Environment.SystemDirectory, "wsl.exe");

    /// <summary>Receives bpp-ctl stderr lines and non-JSON stdout lines (for the installer's terminal pane and the tray log).</summary>
    public event Action<string>? LogLine;

    public async Task<bool> IsWslReadyAsync()
    {
        var r = await TryRunWslAsync("--status").ConfigureAwait(false);
        return r is { ExitCode: 0 };
    }

    /// <summary>
    /// Whether OUR distro is registered. A distro that only shares the name (see <see cref="DistroOwnerAsync"/>)
    /// does not count, so nothing that stops, unregisters or drives "the BPP distro" ever touches it.
    /// </summary>
    public async Task<bool> DistroExistsAsync() => await DistroOwnerAsync().ConfigureAwait(false) == DistroOwner.Ours;

    /// <summary>Whose distro is registered under our name: listed by wsl.exe, then located through the registry.</summary>
    public async Task<DistroOwner> DistroOwnerAsync()
    {
        var listed = await ListedAsync("-l", "-q").ConfigureAwait(false);
        return DistroOwnership.Classify(listed, listed ? RegisteredDistroDir() : null, AppPaths.DistroDir);
    }

    /// <summary>
    /// The directory in which WSL keeps the distro registered under our name (<c>BasePath</c> of its entry under
    /// HKCU\…\Lxss — WSL distros are per user), or null when there is no such entry or it cannot be read.
    /// </summary>
    public static string? RegisteredDistroDir()
    {
        try
        {
            using var lxss = Registry.CurrentUser.OpenSubKey(LxssKey);
            if (lxss == null) return null;
            foreach (var id in lxss.GetSubKeyNames())
            {
                using var distro = lxss.OpenSubKey(id);
                if (distro?.GetValue("DistributionName") is string name
                    && string.Equals(name, AppPaths.DistroName, StringComparison.OrdinalIgnoreCase))
                    return distro.GetValue("BasePath") as string;
            }
            return null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null; // location unknown: DistroOwnership.Classify then goes by the name alone
        }
    }

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

    /// <summary>
    /// "Terminal": a visible console with a root shell in the BPP distro, started in /opt/bpp (bpp-ctl, the
    /// compose file, state/). For looking around and fixing things by hand.
    /// </summary>
    public static void OpenTerminal()
    {
        var psi = new ProcessStartInfo(WslExe) { UseShellExecute = true };
        foreach (var a in new[] { "-d", AppPaths.DistroName, "-u", "root", "--cd", "/opt/bpp" }) psi.ArgumentList.Add(a);
        using var _ = Process.Start(psi);
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

    /// <summary>
    /// Installs or updates the WSL package — what Windows asks for with "wsl --update" right after the features were
    /// enabled. Runs in a visible console window, so the user watches wsl.exe's own download progress (and answers
    /// its UAC prompt for the MSI). Tries <c>--update</c> before <c>--install --no-distribution</c>, each first with
    /// <c>--web-download</c> (GitHub instead of the Store; older wsl.exe rejects the flag). True when one of the
    /// attempts exited with 0.
    /// </summary>
    public async Task<bool> UpdateWslAsync(CancellationToken ct = default)
    {
        string[][] attempts =
        {
            new[] { "--update", "--web-download" },
            new[] { "--update" },
            new[] { "--install", "--no-distribution", "--web-download" },
            new[] { "--install", "--no-distribution" },
        };
        if (!File.Exists(WslExe))
        {
            LogLine?.Invoke("Brak " + WslExe + ".");
            return false;
        }
        foreach (var args in attempts)
        {
            LogLine?.Invoke("wsl " + string.Join(' ', args));
            var psi = new ProcessStartInfo(WslExe) { UseShellExecute = false, CreateNoWindow = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            int code;
            try
            {
                using var p = Process.Start(psi) ?? throw new InvalidOperationException("Nie można uruchomić wsl.exe.");
                await p.WaitForExitAsync(ct).ConfigureAwait(false);
                code = p.ExitCode;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                LogLine?.Invoke(ex.Message);
                return false;
            }
            LogLine?.Invoke($"wsl {string.Join(' ', args)}: kod {code}");
            if (code == 0) return true;
        }
        return false;
    }

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
        return ProcessRunner.Start(psi);
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
