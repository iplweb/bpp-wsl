using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BPP.Core;
using BPP.Windows;
using Microsoft.Win32;

namespace BPP.Tray;

/// <summary>
/// Tray mode: starts the stack (<c>bpp-ctl start --hosts …</c>), runs the LAN relay when the firewall rule
/// exists, follows network changes, and stops the distro on "Zatrzymaj" or logoff.
/// All state lives on the UI thread; background events are posted to it.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private const string TipStarting = "BPP — uruchamianie…";
    private const string TipRunning = "BPP — działa";
    private const string TipError = "BPP — błąd";
    private const string TipStopping = "BPP — zatrzymywanie…";
    private const string ReadyBalloon = "BPP działa — https://localhost:8443/";
    private const int MaxLogLines = 1000;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StartSettleTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TerminateAllowance = TimeSpan.FromSeconds(30);

    private readonly WslClient _wsl = new();
    private readonly Firewall _firewall = new();
    private readonly SynchronizationContext _ui;
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly Icon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _share;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _networkDebounce = new() { Interval = 2000 };
    private readonly SemaphoreSlim _startGate = new(1, 1); // never two `bpp-ctl start` at once
    private readonly CancellationTokenSource _lifetime = new();

    private readonly object _logLock = new();
    private readonly Queue<TrayLogLine> _log = new();
    private long _logSeq;

    private readonly object _shutdownLock = new();
    private Task? _shutdown;
    private volatile bool _closing; // once set, nothing may run `wsl -d BPP` again (it would boot the VM)

    private RelaySet? _relay;
    private bool _running;
    private IReadOnlyList<string>? _startedHosts;
    private LogsForm? _logsForm;

    /// <summary>Set when the user uninstalled from the tray; Program removes the program dir after exit.</summary>
    public bool ProgramDirRemovalRequested { get; private set; }

    public TrayContext()
    {
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _ui = SynchronizationContext.Current!;

        using (var s = typeof(TrayContext).Assembly.GetManifestResourceStream("BPP.Tray.bpp.ico")
                       ?? throw new InvalidOperationException("Brak zasobu bpp.ico."))
            _icon = new Icon(s, SystemInformation.SmallIconSize);

        _menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Otwórz BPP", null, (_, _) => Program.OpenBrowser());
        open.Font = new Font(open.Font, FontStyle.Bold);
        _share = new ToolStripMenuItem("Udostępnij w sieci lokalnej…", null, (_, _) => Fire(ShareOnLanAsync))
        {
            Visible = false,
        };
        _menu.Items.AddRange(new ToolStripItem[]
        {
            open,
            new ToolStripMenuItem("Pokaż logi", null, (_, _) => ShowLogs()),
            _share,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Zatrzymaj", null, (_, _) => Fire(StopAndExitAsync)),
            new ToolStripMenuItem("Odinstaluj", null, (_, _) => Fire(UninstallAsync)),
        });

        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = TipStarting,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => Program.OpenBrowser();

        _wsl.LogLine += Log;
        _networkDebounce.Tick += OnNetworkSettled;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        SystemEvents.SessionEnding += OnSessionEnding;

        Fire(InitAsync);
    }

    // ---- start / network ------------------------------------------------------------------------------

    private async Task InitAsync()
    {
        bool ruleExists;
        try { ruleExists = await Task.Run(_firewall.RuleExists); }
        catch (Exception ex)
        {
            Log("Sprawdzanie reguły zapory: " + ex.Message);
            ruleExists = false;
        }
        if (_closing) return;

        _share.Visible = !ruleExists;
        if (ruleExists) EnableRelay();
        await StartAsync(force: true);
    }

    /// <summary>Runs `bpp-ctl start` for the current host list. Without force, skips when nothing changed.</summary>
    private async Task StartAsync(bool force)
    {
        await _startGate.WaitAsync();
        try
        {
            if (_closing) return;
            var hosts = CurrentHosts();
            if (!force && _startedHosts != null && hosts.SequenceEqual(_startedHosts))
            {
                await UpdateRelayAsync(hosts);
                return;
            }

            var wasRunning = _running;
            SetState(TipStarting, running: false);
            _startedHosts = null;
            await UpdateRelayAsync(hosts);

            string? error = null;
            var ready = false;
            await foreach (var ev in _wsl.RunCtlAsync("start --hosts " + HostList.ToCsv(hosts), _lifetime.Token))
            {
                if (ev.Message.Length > 0) Log($"[{ev.Step}] {ev.Message}");
                if (ev.Step == "ready") ready = true;
                else if (ev.Step == "error") error = ev.Message;
            }
            if (_closing) return;

            if (ready && error == null)
            {
                _startedHosts = hosts;
                SetState(TipRunning, running: true);
                if (!wasRunning) _tray.ShowBalloonTip(5000, "BPP", ReadyBalloon, ToolTipIcon.Info);
                return;
            }
            ReportStartFailure(error ?? "bpp-ctl start zakończył się bez komunikatu „ready”.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // closing: the start client was cancelled on purpose
        }
        catch (Exception ex)
        {
            // anything unexpected (host enumeration, relay, wsl.exe) still ends in the error state
            if (!_closing) ReportStartFailure(ex.Message);
        }
        finally
        {
            _startGate.Release();
        }
    }

    private void ReportStartFailure(string error)
    {
        Log("Uruchamianie nie powiodło się: " + error);
        SetState(TipError, running: false);
        _tray.ShowBalloonTip(10000, "BPP — błąd",
            "Nie udało się uruchomić BPP. Szczegóły: menu ikonki → „Pokaż logi”.", ToolTipIcon.Error);
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
        => _ui.Post(_ =>
        {
            if (_closing) return;
            _networkDebounce.Stop(); // restart the 2 s debounce window
            _networkDebounce.Start();
        }, null);

    private void OnNetworkSettled(object? sender, EventArgs e)
    {
        _networkDebounce.Stop();
        if (!_closing) Fire(() => StartAsync(force: false));
    }

    private static IReadOnlyList<string> CurrentHosts()
    {
        var interfaces = new List<NetInterface>();
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var ipv4 = n.GetIPProperties().UnicastAddresses
                    .Select(u => u.Address)
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .ToList();
                interfaces.Add(new NetInterface(n.Name, n.Description, n.OperationalStatus == OperationalStatus.Up,
                    n.NetworkInterfaceType == NetworkInterfaceType.Loopback, ipv4));
            }
            catch (NetworkInformationException)
            {
                // interface vanished while enumerating
            }
        }
        return HostList.From(interfaces);
    }

    // ---- LAN relay ------------------------------------------------------------------------------------

    /// <summary>Only after the firewall rule exists: binding LAN addresses without it shows a security alert.</summary>
    private void EnableRelay()
    {
        if (_relay != null) return;
        _relay = new RelaySet(AppPaths.Port, new IPEndPoint(IPAddress.Loopback, AppPaths.Port));
        _relay.BindFailed += (address, ex) =>
            Log($"Przekaźnik sieciowy: nie można nasłuchiwać na {address}:{AppPaths.Port}: {ex.Message}");
    }

    private Task UpdateRelayAsync(IReadOnlyList<string> hosts)
        => _relay?.UpdateAsync(RelayAddresses.From(hosts)) ?? Task.CompletedTask;

    private async Task ShareOnLanAsync()
    {
        if (_closing) return;
        _share.Enabled = false;
        int? code;
        try
        {
            // UAC elevates System32 netsh.exe only (Ruling R8); the rule names the installed BPP.exe
            code = await Task.Run(() => _firewall.AddElevated(AppPaths.TrayExe));
        }
        catch (Exception ex)
        {
            Log("Udostępnianie w sieci: " + ex.Message);
            code = -1;
        }
        finally
        {
            _share.Enabled = true;
        }
        if (_closing) return;

        if (code != 0)
        {
            var why = code == null ? "brak zgody administratora" : $"netsh: kod {code}";
            MessageBox.Show($"Nie udało się dodać reguły zapory ({why}).\n\n" +
                            "BPP pozostaje dostępne tylko na tym komputerze.",
                "BPP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _share.Visible = false;
        EnableRelay();
        await UpdateRelayAsync(_startedHosts ?? CurrentHosts());
        _tray.ShowBalloonTip(5000, "BPP", "BPP jest dostępne w sieci lokalnej.", ToolTipIcon.Info);
    }

    // ---- logs -----------------------------------------------------------------------------------------

    /// <summary>Thread-safe: WslClient raises LogLine from its stdout and stderr pumps concurrently.</summary>
    private void Log(string text)
    {
        TrayLogLine line;
        lock (_logLock)
        {
            line = new TrayLogLine(++_logSeq, $"{DateTime.Now:HH:mm:ss} {text}");
            _log.Enqueue(line);
            while (_log.Count > MaxLogLines) _log.Dequeue();
        }
        _ui.Post(_ => _logsForm?.AppendLine(line), null);
    }

    private IReadOnlyList<TrayLogLine> LogSnapshot()
    {
        lock (_logLock) return _log.ToArray();
    }

    private void ShowLogs()
    {
        if (_closing) return;
        if (_logsForm is { IsDisposed: false })
        {
            _logsForm.Activate();
            return;
        }
        var form = new LogsForm(_wsl, LogSnapshot, _icon);
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_logsForm, form)) _logsForm = null;
            form.Dispose();
        };
        _logsForm = form;
        form.Show();
    }

    // ---- stop / uninstall / logoff --------------------------------------------------------------------

    private async Task StopAndExitAsync()
    {
        DetachUi();
        _tray.Text = TipStopping;
        await ShutdownOnceAsync(StartSettleTimeout);
        Exit();
    }

    private async Task UninstallAsync()
    {
        if (_closing || !Uninstaller.Confirm()) return;
        DetachUi();
        _tray.Visible = false;
        await SettleStartAsync(StartSettleTimeout, releaseGate: true); // the uninstaller then stops the stack
        if (_relay != null) await _relay.DisposeAsync();

        try
        {
            await Uninstaller.RunWithProgressAsync();
            ProgramDirRemovalRequested = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Odinstalowanie przerwane: " + ex.Message, "BPP — odinstalowanie",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        Exit();
    }

    /// <summary>Logoff / shutdown: stop the stack before Windows tears WSL down. May run on any thread.</summary>
    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        BeginClosing();
        // On the UI thread an in-flight start cannot make progress while we block, so don't wait for it.
        var startWait = Environment.CurrentManagedThreadId == _uiThreadId ? TimeSpan.Zero : StartSettleTimeout;
        // ShutdownCoreAsync never needs the UI thread, so blocking here (even on it) cannot deadlock.
        // The wait covers every bounded phase: start settle + stop + terminate.
        try { ShutdownOnceAsync(startWait).Wait(startWait + StopTimeout + TerminateAllowance); }
        catch (AggregateException) { }
        // Logoff may still be cancelled by another app: don't stay behind as a zombie holding the mutex.
        _ui.Post(_ => Exit(), null);
    }

    /// <summary>Thread-agnostic part of closing: no new starts, no more network/logoff events.</summary>
    private void BeginClosing()
    {
        _closing = true;
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
    }

    /// <summary>
    /// Gives an in-flight `bpp-ctl start` up to <paramref name="wait"/> to finish (a `stop` racing a start
    /// inside the VM can leave it half up), then cancels the client if it is still running.
    /// Returns whether the start gate was acquired (and is still held unless <paramref name="releaseGate"/>).
    /// </summary>
    private async Task<bool> SettleStartAsync(TimeSpan wait, bool releaseGate)
    {
        var acquired = await _startGate.WaitAsync(wait).ConfigureAwait(false);
        _lifetime.Cancel();
        if (acquired && releaseGate) _startGate.Release();
        return acquired;
    }

    /// <summary>UI thread: also stops the debounce timer and closes windows that could call bpp-ctl.</summary>
    private void DetachUi()
    {
        BeginClosing();
        _networkDebounce.Stop();
        _logsForm?.Close();
        _menu.Enabled = false;
    }

    private Task ShutdownOnceAsync(TimeSpan startWait)
    {
        lock (_shutdownLock) return _shutdown ??= ShutdownCoreAsync(startWait);
    }

    /// <summary>
    /// Cancelling the start client does not stop bpp-ctl inside the VM, so always `bpp-ctl stop`, then
    /// `wsl --terminate BPP`. Never touches the UI (see OnSessionEnding).
    /// </summary>
    private async Task ShutdownCoreAsync(TimeSpan startWait)
    {
        await SettleStartAsync(startWait, releaseGate: true).ConfigureAwait(false);

        var relay = _relay;
        if (relay != null) await relay.DisposeAsync().ConfigureAwait(false);

        if (!await _wsl.DistroExistsAsync().ConfigureAwait(false)) return;

        using (var timeout = new CancellationTokenSource(StopTimeout))
        {
            try { await _wsl.RunCtlTextAsync("stop", timeout.Token).ConfigureAwait(false); }
            catch (Exception ex) { Log("bpp-ctl stop: " + ex.Message); }
        }
        try { await _wsl.TerminateAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log("wsl --terminate: " + ex.Message); }
    }

    private void Exit()
    {
        _tray.Visible = false;
        ExitThread();
    }

    private void SetState(string tooltip, bool running)
    {
        _running = running;
        _tray.Text = tooltip;
    }

    /// <summary>Fire-and-forget on the UI thread; an unexpected failure is logged, never crashes the tray.</summary>
    private async void Fire(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Log("Błąd: " + ex.Message); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            BeginClosing();
            _lifetime.Cancel();
            _wsl.LogLine -= Log;
            _networkDebounce.Dispose();
            _tray.Dispose();
            _menu.Dispose();
            _icon.Dispose();
            _lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
