using System.Diagnostics;
using System.Runtime.Versioning;

namespace BPP.Windows;

/// <summary>
/// Keeps the BPP distro running. WSL stops a distro a few seconds after its last wsl.exe client is gone — with
/// systemd, Docker and every BPP service still up: it runs "systemctl poweroff", waits 10 s and powers the
/// instance off, which kills PostgreSQL mid-shutdown. So for as long as BPP should run, one idle client stays
/// attached: <c>wsl -d BPP -u root -- cat</c>, reading a pipe that we hold open and never write to.
/// The pipe ties the client to this process: when BPP.exe ends in any way (also a crash), the pipe closes,
/// cat sees end of input and exits, and WSL is free to stop the distro again.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DistroKeepAlive : IDisposable
{
    /// <summary>A client that ends sooner than this did not attach (no such distro, WSL broken).</summary>
    private static readonly TimeSpan MinHealthyLifetime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(2);
    private const int MaxQuickFailures = 3;

    private readonly object _lock = new();
    private Process? _process;
    private Stopwatch? _clock;
    private bool _wanted;
    private int _quickFailures;

    /// <summary>What happened to the client (started, lost, given up); raised from thread-pool threads too.</summary>
    public event Action<string>? Log;

    /// <summary>Attaches the client unless it already is attached. Starts the distro when it is stopped.</summary>
    public void Start()
    {
        lock (_lock)
        {
            _wanted = true;
            _quickFailures = 0;
            if (_process is { HasExited: false }) return;
            StartClient();
        }
    }

    /// <summary>Detaches the client; WSL may then stop the distro. <see cref="Start"/> attaches it again.</summary>
    public void Stop()
    {
        Process? p;
        lock (_lock)
        {
            _wanted = false;
            p = _process;
            _process = null;
        }
        if (p == null) return;
        try
        {
            p.StandardInput.Close(); // end of input: cat exits by itself
            if (!p.WaitForExit(2000)) ProcessRunner.TryKill(p);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ProcessRunner.TryKill(p); // the pipe or the process is already gone
        }
        finally
        {
            p.Dispose();
        }
    }

    public void Dispose() => Stop();

    /// <summary>Call with <see cref="_lock"/> held.</summary>
    private void StartClient()
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wsl.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true, // the pipe that keeps cat waiting; never written to
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "-d", AppPaths.DistroName, "-u", "root", "--", "cat" }) psi.ArgumentList.Add(a);
        psi.Environment["WSL_UTF8"] = "1"; // wsl.exe's own messages (logged below) as UTF-8, not UTF-16
        psi.StandardErrorEncoding = System.Text.Encoding.UTF8;
        psi.StandardOutputEncoding = System.Text.Encoding.UTF8;

        Process p;
        try
        {
            p = Process.Start(psi) ?? throw new InvalidOperationException("Nie można uruchomić wsl.exe.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log?.Invoke("Podtrzymanie dystrybucji BPP: " + ex.Message);
            return;
        }
        // nothing is expected on these; drained so that a chatty wsl.exe can never block on a full pipe
        p.OutputDataReceived += (_, _) => { };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log?.Invoke("wsl: " + WslTextLine(e.Data)); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) => OnClientExited(p);
        _process = p;
        _clock = Stopwatch.StartNew();
        if (p.HasExited) OnClientExited(p); // ended before the handler was attached
    }

    private static string WslTextLine(string line) => BPP.Core.WslText.CleanLine(line);

    /// <summary>The client ended although nobody asked it to: attach a new one, unless it keeps failing at once.</summary>
    private void OnClientExited(Process p)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_process, p)) return; // stopped on purpose, or already replaced
            _process = null;
            p.Dispose();
            if (!_wanted) return;

            _quickFailures = _clock != null && _clock.Elapsed < MinHealthyLifetime ? _quickFailures + 1 : 0;
            if (_quickFailures >= MaxQuickFailures)
            {
                _wanted = false;
                Log?.Invoke("Podtrzymanie dystrybucji BPP nie działa (wsl.exe kończy się od razu) — rezygnuję. " +
                            "WSL może zatrzymać BPP po kilkunastu sekundach.");
                return;
            }
            Log?.Invoke("Podtrzymanie dystrybucji BPP przerwane — wznawiam.");
        }
        _ = RestartLaterAsync();
    }

    private async Task RestartLaterAsync()
    {
        await Task.Delay(RestartDelay).ConfigureAwait(false);
        lock (_lock)
        {
            if (_wanted && _process == null) StartClient();
        }
    }
}
