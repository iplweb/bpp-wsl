using System.Runtime.InteropServices;
using BPP.Core;
using BPP.Windows;
using Step = BPP.Core.InstallerStep;

namespace BPP.Setup;

/// <summary>
/// Executes the steps chosen by <see cref="InstallerPlan"/> (spec 4.4) on the UI thread; blocking work runs in
/// <c>Task.Run</c>. Never elevates itself: UAC only for System32 dism.exe / netsh.exe (Ruling R8).
/// </summary>
internal sealed class InstallerFlow
{
    public const string RootfsEntry = "rootfs";
    public const string TrayEntry = "BPP.exe";

    private static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(2);

    private readonly SetupForm _ui;
    private readonly string _setupExe;
    private readonly IReadOnlyDictionary<string, PayloadEntry> _payload;
    private readonly WslClient _wsl = new();
    private readonly WindowsFeatures _features = new();
    private readonly Firewall _firewall = new();
    private readonly Registration _registration = new();

    private InstallerState _state = new();
    private IReadOnlyList<string> _preflightProblems = Array.Empty<string>();
    private int? _dismExitCode;
    private string _lastError = "";
    private string _lastImportError = "";
    private CancellationTokenSource? _initCts;

    public InstallerFlow(SetupForm ui, string setupExe, IReadOnlyDictionary<string, PayloadEntry> payload)
    {
        _ui = ui;
        _setupExe = setupExe;
        _payload = payload;
        _wsl.LogLine += _ui.AppendLog; // raised from two pump threads; AppendLog is thread-safe
    }

    public bool IsRunning { get; private set; }

    /// <summary>True while <c>bpp-ctl init</c> runs — the only step that can be cancelled.</summary>
    public bool InitRunning => _initCts != null;

    public void CancelInit() => _initCts?.Cancel();

    public Task RunAsync()
    {
        // A stale "--resume" entry (e.g. the user ran Setup by hand after the restart) must not fire again later.
        try { _registration.ClearRunOnce(); }
        catch (Exception ex) { _ui.AppendLog("RunOnce: " + ex.Message); }
        return LoopAsync();
    }

    public Task RetryAsync()
    {
        _state = InstallerPlan.Retry(_state);
        return LoopAsync();
    }

    private async Task LoopAsync()
    {
        IsRunning = true;
        _ui.ShowProgress();
        try
        {
            while (true)
            {
                var step = InstallerPlan.Next(_state);
                if (InstallerPlan.IsFinal(step))
                {
                    ShowFinal(step);
                    return;
                }
                _ui.AppendLog($"--- {step}");
                _state = (await RunStepAsync(step)) with { Completed = step };
            }
        }
        catch (Exception ex)
        {
            _ui.AppendLog("Błąd: " + ex);
            if (_state.Init == InitOutcome.Ready)
            {
                // the stack is up but nothing (tray, shortcut) can stop it later: don't leave it holding 8443
                try { await StopDistroAsync(); }
                catch (Exception stopEx) { _ui.AppendLog("Zatrzymywanie BPP: " + stopEx.Message); }
            }
            _ui.ShowError("Instalacja nie powiodła się", new[] { ex.Message }, canRetry: false);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task<InstallerState> RunStepAsync(Step step)
    {
        var s = _state;
        switch (step)
        {
            case Step.Preflight:
                _ui.SetStatus("Sprawdzanie wymagań komputera…", null);
                _preflightProblems = await Task.Run(CheckComputer);
                foreach (var p in _preflightProblems) _ui.AppendLog(p);
                return s with { PreflightOk = _preflightProblems.Count == 0 };

            case Step.ExtractTray:
                _ui.SetStatus("Kopiowanie programu BPP…", null);
                await Task.Run(ExtractTray);
                return s;

            case Step.CheckWsl:
                _ui.SetStatus("Sprawdzanie WSL…", null);
                var ready = await _wsl.IsWslReadyAsync();
                var hint = await Task.Run(_features.IsWslFeatureEnabled);
                _ui.AppendLog($"WSL gotowy: {ready}, funkcje Windows włączone (wskazówka): {hint}");
                return s with { WslReady = ready, WslFeatureHint = hint };

            case Step.EnableWsl:
                _ui.SetStatus("Włączanie WSL — potwierdź w oknie administratora (UAC)…", null);
                _dismExitCode = await Task.Run(_features.EnableWslFeaturesElevated);
                _ui.AppendLog("DISM: " + (_dismExitCode?.ToString() ?? "odmowa UAC"));
                return s with { EnableWsl = InstallerPlan.ClassifyEnableWsl(_dismExitCode) };

            case Step.InstallWslPackage:
                _ui.SetStatus("Instalowanie WSL (może potrwać kilka minut)…", null);
                try { await _wsl.InstallWslPackageAsync(); }
                catch (Exception ex) { _ui.AppendLog(ex.Message); } // the following CheckWsl decides
                return s with { WslPackageInstallAttempted = true };

            case Step.ScheduleResume:
                _registration.SetRunOnce($"\"{_setupExe}\" {Program.ResumeArg}");
                return s;

            case Step.CheckDistro:
                _ui.SetStatus("Sprawdzanie poprzedniej instalacji…", null);
                return s with { DistroExists = await _wsl.DistroExistsAsync() };

            case Step.AskReinstall:
                return s with { ReinstallConfirmed = _ui.AskReinstall() };

            case Step.Unregister:
                _ui.SetStatus("Usuwanie poprzedniej instalacji BPP…", null);
                if (await _wsl.DistroExistsAsync()) await _wsl.UnregisterAsync();
                return s;

            case Step.ImportDistro:
                return s with { ImportOk = await ImportAsync() };

            case Step.RunInit:
                return s with { Init = await RunInitAsync() };

            case Step.StopDistro:
                _ui.SetStatus(s.Init == InitOutcome.Cancelled ? "Przerywanie — zatrzymywanie BPP…" : "Zatrzymywanie BPP…", null);
                await StopDistroAsync();
                return s;

            case Step.AddFirewall:
                _ui.SetStatus("Udostępnianie w sieci lokalnej — potwierdź w oknie administratora (UAC)…", null);
                int? code;
                try { code = await Task.Run(() => _firewall.AddElevated(AppPaths.TrayExe)); }
                catch (Exception ex)
                {
                    _ui.AppendLog("Zapora: " + ex.Message);
                    code = -1;
                }
                _ui.AppendLog("Zapora: " + (code?.ToString() ?? "odmowa UAC"));
                return s with { FirewallOk = code == 0 };

            case Step.RegisterUninstall:
                _registration.RegisterUninstall(AppPaths.TrayExe, Version);
                return s;

            case Step.CreateShortcut:
                _ui.SetStatus("Tworzenie skrótu w Menu Start…", null);
                _registration.CreateStartMenuShortcut(AppPaths.TrayExe); // COM: stays on the STA UI thread
                return s;

            default:
                throw new InvalidOperationException($"Nieznany krok {step}.");
        }
    }

    private void ShowFinal(Step step)
    {
        switch (step)
        {
            case Step.Done:
                _ui.ShowDone(lanEnabled: _state.FirewallOk);
                break;
            case Step.RestartRequired:
                _ui.ShowRestart();
                break;
            case Step.PreflightFailed:
                _ui.ShowError("Ten komputer nie spełnia wymagań BPP", _preflightProblems, canRetry: false);
                break;
            case Step.WslNeedsAdmin:
                _ui.ShowError("Nie można włączyć WSL", WithImportError(
                    "Poproś administratora o włączenie WSL (lub o zatwierdzenie okna administratora), " +
                    "a potem uruchom instalator ponownie."), canRetry: false);
                break;
            case Step.WslEnableFailed:
                _ui.ShowError("Nie udało się włączyć WSL", WithImportError(
                    $"Narzędzie DISM zakończyło się kodem {_dismExitCode}.",
                    "Uruchom ponownie komputer i spróbuj jeszcze raz."), canRetry: false);
                break;
            case Step.WslUnavailable:
                _ui.ShowError("WSL nie działa na tym komputerze", WithImportError(
                    "Uruchom ponownie komputer i uruchom instalator jeszcze raz.",
                    "Jeśli to nie pomoże, sprawdź, czy wirtualizacja jest włączona w BIOS/UEFI."), canRetry: false);
                break;
            case Step.ImportFailed:
                _ui.ShowError("Nie udało się zainstalować dystrybucji BPP", new[] { _lastError }, canRetry: false);
                break;
            case Step.InitFailed:
                _ui.ShowError("Konfiguracja BPP nie powiodła się", new[]
                {
                    _lastError,
                    "Sprawdź połączenie z internetem i kliknij „Spróbuj ponownie”.",
                }, canRetry: true);
                break;
            case Step.Declined:
                _ui.ShowError("Instalacja przerwana", new[]
                {
                    "Istniejąca dystrybucja BPP i jej dane pozostały bez zmian.",
                }, canRetry: false);
                break;
            case Step.Cancelled:
                _ui.ShowError("Instalacja przerwana", new[]
                {
                    "Instalacja została anulowana. Uruchom instalator ponownie, aby ją dokończyć.",
                }, canRetry: false);
                break;
            default:
                throw new InvalidOperationException($"Nieznana strona {step}.");
        }
    }

    /// <summary>When these pages follow a failed <c>wsl --import</c>, its real error is shown too.</summary>
    private IReadOnlyList<string> WithImportError(params string[] lines)
        => _lastImportError.Length == 0
            ? lines
            : lines.Append("Błąd importu dystrybucji: " + _lastImportError).ToList();

    // ---- steps ----------------------------------------------------------------------------------------

    private static IReadOnlyList<string> CheckComputer()
    {
        var ports = new Ports();
        var drive = new DriveInfo(Path.GetPathRoot(AppPaths.DistroDir)!);
        var input = new PreflightInput(InstalledRamBytes(), drive.AvailableFreeSpace,
            RuntimeInformation.OSArchitecture, ports.IsPortFree(AppPaths.Port));

        return Preflight.Check(input).Select(p => p switch
        {
            PreflightProblem.NotX64 =>
                $"BPP wymaga 64-bitowego procesora x64 (ten komputer: {RuntimeInformation.OSArchitecture}).",
            PreflightProblem.TooLittleRam =>
                $"Za mało pamięci RAM: {Gb(input.TotalRamBytes)} GB, wymagane co najmniej {Gb(Preflight.MinRam)} GB.",
            PreflightProblem.TooLittleDisk =>
                $"Za mało wolnego miejsca na dysku {drive.Name}: {Gb((ulong)input.FreeDiskBytes)} GB, " +
                $"wymagane co najmniej {Gb((ulong)Preflight.MinDisk)} GB.",
            PreflightProblem.PortBusy => PortBusyMessage(ports.OwnerProcessName(AppPaths.Port)),
            _ => p.ToString(),
        }).ToList();
    }

    private static string PortBusyMessage(string? owner)
        => $"Port {AppPaths.Port} jest zajęty przez " + (owner != null ? $"program „{owner}”." : "inny program.") +
           " Zamknij ten program i uruchom instalator ponownie. Jeśli to działające BPP, zatrzymaj je " +
           "(ikonka BPP w zasobniku → Zatrzymaj).";

    private static string Gb(ulong bytes) => (bytes / (double)(1UL << 30)).ToString("0.#");

    /// <summary>
    /// Installed RAM (what the user knows as "8 GB"); GlobalMemoryStatusEx would report ~7.8 GiB on such a machine
    /// because of hardware-reserved memory and wrongly fail the 8 GiB check.
    /// </summary>
    private static ulong InstalledRamBytes()
        => GetPhysicallyInstalledSystemMemory(out var kb) ? kb * 1024 : (ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    private void ExtractTray()
    {
        Directory.CreateDirectory(AppPaths.ProgramDir);
        var tmp = AppPaths.TrayExe + ".new";
        try
        {
            using (var src = PayloadArchive.Open(_setupExe, _payload[TrayEntry]))
            using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                src.CopyTo(dst);
            File.Move(tmp, AppPaths.TrayExe, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Nie można zapisać pliku {AppPaths.TrayExe} ({ex.Message}). Jeśli BPP jest uruchomione, zatrzymaj je " +
                "(ikonka BPP w zasobniku → Zatrzymaj) i uruchom instalator ponownie.", ex);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    /// <summary>
    /// Extracts the rootfs to %TEMP% and imports it. Only a failing <c>wsl --import</c> returns false (the plan may
    /// then offer enable-wsl); a failing extraction (e.g. full disk) is an ordinary error.
    /// </summary>
    private async Task<bool> ImportAsync()
    {
        var tar = Path.Combine(Path.GetTempPath(), $"BPP-rootfs-{Guid.NewGuid():N}.tar.gz");
        try
        {
            _ui.SetStatus("Rozpakowywanie dystrybucji BPP…", null);
            await Task.Run(() =>
            {
                using var src = PayloadArchive.Open(_setupExe, _payload[RootfsEntry]);
                using var dst = new FileStream(tar, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
                src.CopyTo(dst, 1 << 20);
            });

            _ui.SetStatus("Importowanie dystrybucji BPP (może potrwać kilka minut)…", null);
            try
            {
                await _wsl.ImportAsync(tar);
            }
            catch (InvalidOperationException ex)
            {
                _lastError = _lastImportError = ex.Message;
                _ui.AppendLog(ex.Message);
                await RemovePartialDistroAsync();
                return false;
            }
        }
        finally
        {
            TryDelete(tar);
        }

        await _wsl.TrySetSparseAsync();
        return true;
    }

    private async Task<InitOutcome> RunInitAsync()
    {
        using var cts = new CancellationTokenSource();
        _initCts = cts;
        _ui.SetCancelEnabled(true);
        _ui.SetStatus("Konfiguracja i pobieranie BPP…", null);
        try
        {
            var hosts = HostList.ToCsv(LocalHosts.Current());
            string? error = null;
            var ready = false;
            await foreach (var ev in _wsl.RunCtlAsync("init --hosts " + hosts, cts.Token))
            {
                _ui.AppendLog($"[{ev.Step}] {ev.Message}");
                if (ev.Step == "error") error = ev.Message;
                else
                {
                    if (ev.Step == "ready") ready = true;
                    _ui.SetStatus(ev.Message, ev.Progress);
                }
            }
            if (cts.IsCancellationRequested) return InitOutcome.Cancelled;
            if (ready && error == null) return InitOutcome.Ready;
            _lastError = error ?? "bpp-ctl init zakończył się bez komunikatu „ready”.";
            return InitOutcome.Failed;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return InitOutcome.Cancelled;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            _ui.AppendLog("bpp-ctl init: " + ex);
            if (cts.IsCancellationRequested) return InitOutcome.Cancelled;
            _lastError = ex.Message;
            return InitOutcome.Failed;
        }
        finally
        {
            _initCts = null;
            _ui.SetCancelEnabled(false);
        }
    }

    /// <summary>A failed import may leave BPP registered; remove it so a retry is not asked "Zainstalować ponownie?".</summary>
    private async Task RemovePartialDistroAsync()
    {
        try
        {
            if (await _wsl.DistroExistsAsync()) await _wsl.UnregisterAsync();
        }
        catch (Exception ex) { _ui.AppendLog("Usuwanie niepełnej dystrybucji: " + ex.Message); }
    }

    /// <summary>Cancelling the init client does not stop bpp-ctl inside the VM: `bpp-ctl stop`, then terminate.</summary>
    private async Task StopDistroAsync()
    {
        if (!await _wsl.DistroExistsAsync()) return;
        using (var timeout = new CancellationTokenSource(StopTimeout))
        {
            try { await _wsl.RunCtlTextAsync("stop", timeout.Token); }
            catch (Exception ex) { _ui.AppendLog("bpp-ctl stop: " + ex.Message); }
        }
        try { await _wsl.TerminateAsync(); }
        catch (Exception ex) { _ui.AppendLog("wsl --terminate: " + ex.Message); }
    }

    private static string Version
        => typeof(InstallerFlow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
}
