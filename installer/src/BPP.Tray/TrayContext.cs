using System.Net;
using System.Net.NetworkInformation;
using BPP.Core;
using BPP.Windows;
using Microsoft.Win32;

namespace BPP.Tray;

/// <summary>
/// Tray mode: starts the stack (<c>bpp-ctl start --hosts …</c>), runs the LAN relay when the firewall rule
/// exists, follows network changes, and stops the distro on "Zatrzymaj" or logoff. While it runs it asks
/// every few seconds how BPP is doing (WSL distro, Docker, services, website) and shows the answer as a
/// coloured dot on the icon, in the tooltip and in the first lines of the menu.
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
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(30);
    private const int StatusIntervalMs = 20_000;
    private const string StatusUnknown = "sprawdzanie…";
    private static readonly string[] StatusLabels = { "Dystrybucja WSL", "Docker", "Usługi", "Strona" };

    private readonly WslClient _wsl = new();
    private readonly DistroKeepAlive _keepAlive = new();
    private readonly Firewall _firewall = new();
    private readonly SynchronizationContext _ui;
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly Icon _icon;
    private readonly StatusIcons _statusIcons;
    private readonly SiteProbe _site = new(Program.Url);
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem[] _statusItems;
    private readonly ToolStripMenuItem _restart;
    private readonly ToolStripMenuItem _autostart;
    private readonly ToolStripMenuItem _version;
    private static readonly string ProgramVersion
        = typeof(TrayContext).Assembly.GetName().Version?.ToString(3) ?? "?";
    private readonly Registration _registration = new();
    private readonly ToolStripMenuItem _share;
    private readonly ToolStripMenuItem _trust;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _networkDebounce = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = StatusIntervalMs };
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
    private bool _starting; // a `bpp-ctl start` is in flight: it owns the icon and the tooltip
    private bool _polling;
    private int _badPolls; // consecutive status checks that found a problem
    private bool _announceProblem; // BPP was fine since the last balloon, so the next problem is news
    private string? _statusError;
    private IReadOnlyList<string>? _startedHosts;
    private LogsForm? _logsForm;

    /// <summary>
    /// Set when the user uninstalled from the tray and the distro is gone; Program removes the program dir after exit.
    /// </summary>
    public bool ProgramDirRemovalRequested { get; private set; }

    public TrayContext()
    {
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _ui = SynchronizationContext.Current!;

        using (var s = typeof(TrayContext).Assembly.GetManifestResourceStream("BPP.Tray.bpp.ico")
                       ?? throw new InvalidOperationException("Brak zasobu bpp.ico."))
            _icon = new Icon(s, SystemInformation.SmallIconSize);
        _statusIcons = new StatusIcons(_icon);

        _menu = new ContextMenuStrip();
        // a click on a status line opens the logs: that is where one looks next when a line is not green
        _statusItems = StatusLabels
            .Select(label => new ToolStripMenuItem($"{label}: {StatusUnknown}",
                _statusIcons.Dot(StatusLevel.Starting), (_, _) => ShowLogs()))
            .ToArray();
        _restart = new ToolStripMenuItem("Uruchom ponownie", null, (_, _) => Fire(() => StartAsync(force: true)))
        {
            Enabled = false,
        };
        var open = new ToolStripMenuItem("Otwórz BPP", null, (_, _) => Program.OpenBrowser());
        open.Font = new Font(open.Font, FontStyle.Bold);
        _share = new ToolStripMenuItem("Udostępnij w sieci lokalnej…", null, (_, _) => Fire(ShareOnLanAsync))
        {
            Visible = false,
        };
        _trust = new ToolStripMenuItem("Dodaj certyfikat BPP (bez ostrzeżeń w przeglądarce)…", null,
            (_, _) => Fire(TrustCertificateAsync))
        {
            Visible = false,
        };
        _autostart = new ToolStripMenuItem("Uruchamiaj po zalogowaniu do Windows", null, (_, _) => ToggleAutostart());
        _menu.Opening += (_, _) => RefreshAutostartItem(); // the entry may have been changed outside
        // which BPP is running (its images' tag, known after the first status check) and which program this is
        _version = new ToolStripMenuItem(VersionText("")) { Enabled = false };
        _menu.Items.Add(_version);
        _menu.Items.AddRange(_statusItems.Cast<ToolStripItem>().ToArray());
        _menu.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripSeparator(),
            open,
            new ToolStripMenuItem("Pokaż logi", null, (_, _) => ShowLogs()),
            new ToolStripMenuItem("Terminal", null, (_, _) => OpenTerminal()),
            new ToolStripMenuItem("Wyślij diagnostykę do IPLweb…", null, (_, _) => SendDiagnostics(null)),
            _share,
            _trust,
            _autostart,
            new ToolStripSeparator(),
            _restart,
            new ToolStripMenuItem("Zatrzymaj", null, (_, _) => Fire(StopAndExitAsync)),
            new ToolStripMenuItem("Odinstaluj", null, (_, _) => Fire(UninstallAsync)),
        });

        _tray = new NotifyIcon
        {
            Icon = _statusIcons.Icon(StatusLevel.Starting),
            Text = TipStarting,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => Program.OpenBrowser();

        _wsl.LogLine += Log;
        _keepAlive.Log += Log;
        _networkDebounce.Tick += OnNetworkSettled;
        _statusTimer.Tick += (_, _) => Fire(PollStatusAsync);
        _statusTimer.Start();
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
            var hosts = LocalHosts.Current();
            if (!force && _startedHosts != null && hosts.SequenceEqual(_startedHosts))
            {
                await UpdateRelayAsync(hosts);
                return;
            }

            var wasRunning = _running;
            _starting = true;
            _restart.Enabled = false;
            _announceProblem = false;
            SetState(TipStarting, StatusLevel.Starting, running: false);
            _startedHosts = null;
            await UpdateRelayAsync(hosts);

            // before the start and for as long as the tray runs: without a client WSL stops the distro
            _keepAlive.Start();

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
                _starting = false;
                SetState(TipRunning, StatusLevel.Ok, running: true);
                if (!wasRunning) _tray.ShowBalloonTip(5000, "BPP", ReadyBalloon, ToolTipIcon.Info);
                Fire(PollStatusAsync); // fill in the status lines now, not at the next tick
                await RefreshTrustItemAsync();
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
            _starting = false;
            if (!_closing) _restart.Enabled = true;
            _startGate.Release();
        }
    }

    private void ReportStartFailure(string error)
    {
        Log("Uruchamianie nie powiodło się: " + error);
        SetState(TipError, StatusLevel.Bad, running: false);
        _tray.ShowBalloonTip(10000, "BPP — błąd",
            "Nie udało się uruchomić BPP. Szczegóły: menu ikonki → „Pokaż logi” (tam też „Wyślij diagnostykę do IPLweb…”).", ToolTipIcon.Error);
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

    // ---- status ---------------------------------------------------------------------------------------

    /// <summary>
    /// One round of questions: is the distro running (asked without starting it), what does
    /// <c>bpp-ctl status</c> say about Docker and the services, does the website answer. `bpp-ctl` is asked
    /// only when the distro runs — `wsl -d BPP` would start a stopped one — and never while closing.
    /// </summary>
    private async Task PollStatusAsync()
    {
        if (_closing || _polling) return;
        _polling = true;
        try
        {
            var token = _lifetime.Token;
            var siteTask = _site.StatusCodeAsync(token);
            var distroRunning = await _wsl.IsDistroRunningAsync();
            StackStatus? stack = null;
            if (distroRunning && !_closing) stack = await AskStackAsync(token);
            var site = await siteTask;
            if (_closing) return;
            if (stack is { ImageTag.Length: > 0 }) _version.Text = VersionText(stack.ImageTag);
            ShowStatus(StatusReport.Build(distroRunning, stack, site));
        }
        catch (OperationCanceledException) when (_closing)
        {
            // closing: the clients were cancelled on purpose
        }
        finally
        {
            _polling = false;
        }
    }

    /// <summary>Null when bpp-ctl gave no usable answer; why goes to the log, once per kind of failure.</summary>
    private async Task<StackStatus?> AskStackAsync(CancellationToken lifetime)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        timeout.CancelAfter(StatusTimeout);
        string error;
        try
        {
            var stack = await _wsl.StatusAsync(timeout.Token);
            _statusError = null;
            return stack;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error = ex.Message;
        }
        catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
        {
            error = $"bpp-ctl status nie odpowiedział w ciągu {StatusTimeout.TotalSeconds:0} s.";
        }
        if (error != _statusError) Log("Sprawdzanie stanu: " + error);
        _statusError = error;
        return null;
    }

    private static string VersionText(string imageTag)
        => imageTag.Length > 0 ? $"BPP {imageTag} (program {ProgramVersion})" : $"BPP (program {ProgramVersion})";

    private void ShowStatus(StatusReport report)
    {
        for (var i = 0; i < _statusItems.Length && i < report.Lines.Count; i++)
        {
            _statusItems[i].Text = report.Lines[i].Text;
            _statusItems[i].Image = _statusIcons.Dot(report.Lines[i].Level);
        }
        if (_starting) return; // the icon and the tooltip say "uruchamianie" until the start ends

        _tray.Icon = _statusIcons.Icon(report.Level);
        _tray.Text = report.Tooltip;

        if (report.Level != StatusLevel.Bad)
        {
            _badPolls = 0;
            if (report.Level == StatusLevel.Ok) _announceProblem = true;
            return;
        }
        // twice in a row: one slow answer under load is not worth a balloon
        if (++_badPolls < 2 || !_announceProblem) return;
        _announceProblem = false;
        var cause = report.Lines.First(l => l.Level == StatusLevel.Bad).Text;
        Log("BPP przestało działać poprawnie — " + string.Join("; ", report.Lines.Select(l => l.Text)));
        _tray.ShowBalloonTip(10000, "BPP — problem",
            $"{cause}. Menu ikonki → „Uruchom ponownie”; szczegóły w „Pokaż logi”.", ToolTipIcon.Warning);
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
        if (MessageBox.Show(
                "Aby BPP było widoczne z innych komputerów w sieci lokalnej, trzeba dodać regułę zapory Windows " +
                "(port 8443).\n\nPo kliknięciu OK Windows pokaże okno administratora z pytaniem o zgodę. Aplikacja " +
                "nazywa się tam „Network Command Shell” (wydawca: Microsoft Windows) — to systemowe narzędzie do " +
                "ustawień sieci. Kliknij w nim „Tak”.",
                "BPP — udostępnianie w sieci lokalnej", MessageBoxButtons.OKCancel, MessageBoxIcon.Information)
            != DialogResult.OK) return;
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
        await UpdateRelayAsync(_startedHosts ?? LocalHosts.Current());
        _tray.ShowBalloonTip(5000, "BPP", "BPP jest dostępne w sieci lokalnej.", ToolTipIcon.Info);
    }

    // ---- certificate ----------------------------------------------------------------------------------

    /// <summary>
    /// Offers "Dodaj certyfikat BPP…" only while the distro's CA is not among the user's trusted roots (declined
    /// in the installer, or the distro comes from a version without the local CA). Never prompts by itself.
    /// </summary>
    private async Task RefreshTrustItemAsync()
    {
        try
        {
            using var ca = await TrustedRoot.ReadFromDistroAsync(_wsl, _lifetime.Token);
            var trusted = await Task.Run(() => TrustedRoot.IsTrusted(ca));
            if (!_closing) _trust.Visible = !trusted;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Security.Cryptography.CryptographicException
                                       or IOException or System.ComponentModel.Win32Exception)
        {
            Log("Certyfikat: " + ex.Message);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // closing
        }
    }

    private async Task TrustCertificateAsync()
    {
        if (_closing) return;
        _trust.Enabled = false;
        try
        {
            using var ca = await TrustedRoot.ReadFromDistroAsync(_wsl, _lifetime.Token);
            // Windows shows its own "Security Warning" dialog; nothing is elevated
            var outcome = await Task.Run(() => TrustedRoot.Trust(ca));
            Log("Certyfikat: " + outcome);
            if (_closing || outcome == TrustOutcome.Declined) return;
            _trust.Visible = false;
            _tray.ShowBalloonTip(5000, "BPP", "Certyfikat BPP dodany. Uruchom przeglądarkę ponownie, " +
                                                "jeśli nadal ostrzega.", ToolTipIcon.Info);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Security.Cryptography.CryptographicException
                                       or IOException or System.ComponentModel.Win32Exception)
        {
            Log("Certyfikat: " + ex.Message);
            if (!_closing)
                MessageBox.Show("Nie udało się dodać certyfikatu BPP:\n\n" + ex.Message, "BPP",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // closing
        }
        finally
        {
            _trust.Enabled = true;
        }
    }

    // ---- autostart ------------------------------------------------------------------------------------

    private void RefreshAutostartItem()
    {
        try
        {
            _autostart.Checked = _registration.IsAutostartEnabled();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log("Autostart: " + ex.Message);
        }
    }

    private void ToggleAutostart()
    {
        if (_closing) return;
        try
        {
            _registration.SetAutostart(AppPaths.TrayExe, !_registration.IsAutostartEnabled());
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log("Autostart: " + ex.Message);
            MessageBox.Show("Nie udało się zmienić ustawienia autostartu: " + ex.Message, "BPP",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshAutostartItem();
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
        var form = new LogsForm(_wsl, LogSnapshot, _icon, SendDiagnostics);
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_logsForm, form)) _logsForm = null;
            form.Dispose();
        };
        _logsForm = form;
        form.Show();
    }

    /// <summary>A root shell in the BPP distro, in its own console window; not while closing (it would boot the VM).</summary>
    private void OpenTerminal()
    {
        if (_closing) return;
        try
        {
            WslClient.OpenTerminal();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log("Terminal: " + ex.Message);
            MessageBox.Show("Nie udało się otworzyć terminala: " + ex.Message, "BPP", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// The ZIP for IPLweb support. The tray keeps BPP running, so diag never stops the distro; while the tray is
    /// closing nothing may boot it again.
    /// </summary>
    private async void SendDiagnostics(IWin32Window? owner)
    {
        if (_closing) return;
        var log = string.Join(Environment.NewLine, LogSnapshot().Select(l => l.Text));
        await DiagnosticsSender.SendAsync(owner,
            () => Diagnostics.CreateZipAsync("ikonka", log, _wsl, null, stopDistroIfStarted: false));
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
        _keepAlive.Stop();
        if (_relay != null) await _relay.DisposeAsync();

        try
        {
            var report = await Uninstaller.RunWithProgressAsync();
            // the distro is still there: keep everything (also BPP.exe) so the uninstall can be retried
            ProgramDirRemovalRequested = report.Cleanup.RemoveProgramDir;
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
        _statusTimer.Stop();
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
        _keepAlive.Stop(); // only now: the services are down, so WSL may stop the distro
        try { await _wsl.TerminateAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log("wsl --terminate: " + ex.Message); }
    }

    private void Exit()
    {
        _tray.Visible = false;
        ExitThread();
    }

    private void SetState(string tooltip, StatusLevel level, bool running)
    {
        _running = running;
        _tray.Text = tooltip;
        _tray.Icon = _statusIcons.Icon(level);
    }

    /// <summary>Fire-and-forget on the UI thread; an unexpected failure is logged, never crashes the tray.</summary>
    private async void Fire(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Log("Błąd: " + ex.Message); }
    }

    private bool _disposed;

    /// <summary>
    /// Runs twice on every exit: Application.Run disposes its context when the message loop ends, and so does
    /// the <c>using</c> in Program.Main. The second pass must do nothing — cancelling the already disposed
    /// <see cref="_lifetime"/> threw, so BPP.exe crashed on exit and never reached what follows the loop
    /// (removing the program directory after an uninstall).
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            BeginClosing();
            _lifetime.Cancel();
            _keepAlive.Dispose();
            _keepAlive.Log -= Log;
            _wsl.LogLine -= Log;
            _networkDebounce.Dispose();
            _statusTimer.Dispose();
            _site.Dispose();
            _tray.Dispose();
            _menu.Dispose();
            _statusIcons.Dispose();
            _icon.Dispose();
            _lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
