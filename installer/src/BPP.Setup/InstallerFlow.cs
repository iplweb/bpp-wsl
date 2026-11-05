using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using BPP.Core;
using BPP.Core.Download;
using BPP.Windows;
using Step = BPP.Core.InstallerStep;

namespace BPP.Setup;

/// <summary>
/// Executes the steps chosen by <see cref="InstallerPlan"/> (spec 4.4) on the UI thread; blocking work runs in
/// <c>Task.Run</c>. Never elevates itself: UAC only for System32 dism.exe / netsh.exe (Ruling R8).
/// </summary>
internal sealed class InstallerFlow
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(2);
    private const string RootfsTempPrefix = "BPP-rootfs-";

    private readonly SetupForm _ui;
    private readonly string _setupExe;
    private readonly AppArgs _args;
    private string? _rootfsPath; // verified archive for wsl --import (set by DownloadRootfs)
    private readonly WslClient _wsl = new();
    private readonly WindowsFeatures _features = new();
    private readonly Firewall _firewall = new();
    private readonly Registration _registration = new();

    private InstallerState _state;
    private IReadOnlyList<string> _preflightProblems = Array.Empty<string>();
    private int? _dismExitCode;
    private string _lastError = "";
    private string _lastImportError = "";
    private CancellationTokenSource? _initCts;
    private string? _failureDiag; // bpp-ctl diag taken after a failed init, before the stack was stopped

    public InstallerFlow(SetupForm ui, string setupExe, AppArgs args)
    {
        _ui = ui;
        _setupExe = setupExe;
        _args = args;
        // "--install-anyway" (after a restart): the user already accepted too little RAM / disk
        _state = new InstallerState { InstallAnyway = args.InstallAnyway };
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
        SweepStaleFiles();
        return LoopAsync();
    }

    public Task RetryAsync()
    {
        _state = InstallerPlan.Retry(_state);
        return LoopAsync();
    }

    /// <summary>"Zatrzymaj BPP" on the preflight page: stop the running distro, then check the computer again.</summary>
    public Task StopBppAsync()
    {
        _state = InstallerPlan.StopBpp(_state);
        return LoopAsync();
    }

    /// <summary>"Zainstaluj mimo to" on the preflight page (too little RAM / disk only).</summary>
    public Task InstallAnywayAsync()
    {
        _state = InstallerPlan.InstallAnywayFrom(_state);
        _ui.AppendLog("Użytkownik wybrał „Zainstaluj mimo to”.");
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
                _ui.SetOverall(InstallProgress.AtStart(step));
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
                var check = await Task.Run(CheckComputer);
                // a WSL process on 8443 + the BPP distro: most likely BPP left running without the tray
                var bppDistro = check.Problems.Contains(PreflightProblem.PortBusy)
                                && Preflight.IsWslProcess(check.PortOwner) && await _wsl.DistroExistsAsync();
                var heldByBpp = Preflight.PortHeldByBpp(check.Problems, check.PortOwner, bppDistro);
                _preflightProblems = Describe(check, heldByBpp, s.StopBppAttempted);
                foreach (var p in _preflightProblems) _ui.AppendLog(p);
                return s with
                {
                    PreflightOk = check.Problems.Count == 0,
                    PreflightSoftOnly = Preflight.OnlySoft(check.Problems),
                    PortHeldByBpp = heldByBpp,
                };

            case Step.StopRunningBpp:
                _ui.SetStatus("Zatrzymywanie BPP…", null);
                await StopDistroAsync();
                return s with { StopBppAttempted = true };

            case Step.InstallProgram:
                _ui.SetStatus("Kopiowanie programu BPP…", null);
                await Task.Run(InstallProgram);
                return s;

            case Step.DownloadRootfs:
                return s with { RootfsReady = await DownloadRootfsAsync() };

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
                _ui.SetStatus("Aktualizowanie WSL — postęp widać w oknie konsoli (może pojawić się też okno " +
                              "administratora)…", null);
                // the following CheckWsl / ImportDistro decide
                var updated = await _wsl.UpdateWslAsync();
                _ui.AppendLog("Aktualizacja WSL: " + (updated ? "zakończona" : "nieudana"));
                _ui.SetStatus("Sprawdzanie WSL…", null);
                return s with { WslPackageInstallAttempted = true };

            case Step.ScheduleResume:
                // BPP.exe in the program folder: the copy the mini installer downloaded may be gone after the restart
                _registration.SetRunOnce(
                    $"\"{AppPaths.TrayExe}\" {(_args with { InstallAnyway = s.InstallAnyway }).ResumeCommandArgs()}");
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
                if (s.Init == InitOutcome.Failed)
                {
                    // the healthcheck history of the containers is what support needs; stopping them loses it
                    _ui.SetStatus("Zbieranie diagnostyki…", null);
                    _failureDiag = await Diagnostics.BppDiagAsync(_wsl, stopDistroIfStarted: false);
                }
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
                var anyway = InstallerPlan.CanInstallAnyway(_state);
                _ui.ShowError("Ten komputer nie spełnia wymagań BPP", anyway
                        ? _preflightProblems.Append(
                            "Możesz zainstalować BPP mimo to (przycisk „Zainstaluj mimo to”), ale instalacja może się " +
                            "nie udać, a BPP może działać wolno lub zatrzymać się z braku pamięci albo miejsca.").ToList()
                        : _preflightProblems,
                    canRetry: false, canStopBpp: InstallerPlan.CanStopBpp(_state), canInstallAnyway: anyway);
                break;
            case Step.DownloadFailed:
                _ui.ShowError("Nie udało się pobrać dystrybucji BPP", new[]
                {
                    _lastError,
                    "Sprawdź połączenie z internetem i kliknij „Spróbuj ponownie” — pobieranie zostanie wznowione " +
                    "od miejsca, w którym się przerwało.",
                    "Jeśli sieć blokuje GitHub (np. serwer pośredniczący w firmie lub na uczelni), pobierz plik " +
                    $"{RootfsAssetName} z zakładki Releases na innym komputerze, połóż go obok BPP-Setup.exe " +
                    "i uruchom instalator ponownie.",
                }, canRetry: true);
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
                    "Kliknij „Spróbuj ponownie”. Obrazy, które już się pobrały, nie są pobierane drugi raz — " +
                    "jeśli pobieranie się udało, ponowienie tylko uruchomi usługi.",
                    "Usługi możesz też uruchomić ręcznie: otwórz Wiersz polecenia (cmd) lub PowerShell i wpisz:\r\n" +
                    "    wsl -d BPP bpp-ctl start\r\n" +
                    "Stan usług:  wsl -d BPP bpp-ctl compose ps\r\n" +
                    "Logi usługi: wsl -d BPP bpp-ctl compose logs workerserver\r\n" +
                    "Gdy usługi działają, kliknij „Spróbuj ponownie”, aby dokończyć instalację (skrót, zapora).",
                    "Jeśli problem się powtarza, kliknij „Wyślij diagnostykę do IPLweb…”.",
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

    private sealed record ComputerCheck(
        PreflightInput Input, IReadOnlyList<PreflightProblem> Problems, string DriveName, string? PortOwner);

    private static ComputerCheck CheckComputer()
    {
        var ports = new Ports();
        var drive = new DriveInfo(Path.GetPathRoot(AppPaths.DistroDir)!);
        var input = new PreflightInput(InstalledRamBytes(), drive.AvailableFreeSpace,
            RuntimeInformation.OSArchitecture, ports.IsPortFree(AppPaths.Port));
        var problems = Preflight.Check(input);
        var owner = problems.Contains(PreflightProblem.PortBusy) ? ports.OwnerProcessName(AppPaths.Port) : null;
        return new ComputerCheck(input, problems, drive.Name, owner);
    }

    private static IReadOnlyList<string> Describe(ComputerCheck check, bool heldByBpp, bool stopAttempted)
    {
        var input = check.Input;
        return check.Problems.Select(p => p switch
        {
            PreflightProblem.NotX64 =>
                $"BPP wymaga 64-bitowego procesora x64 (ten komputer: {RuntimeInformation.OSArchitecture}).",
            PreflightProblem.TooLittleRam =>
                $"Za mało pamięci RAM: {Gb(input.TotalRamBytes)} GB, wymagane co najmniej {Gb(Preflight.MinRam)} GB.",
            PreflightProblem.TooLittleDisk =>
                $"Za mało wolnego miejsca na dysku {check.DriveName}: {Gb((ulong)input.FreeDiskBytes)} GB, " +
                $"wymagane co najmniej {Gb((ulong)Preflight.MinDisk)} GB.",
            PreflightProblem.PortBusy => PortBusyMessage(check.PortOwner, heldByBpp, stopAttempted),
            _ => p.ToString(),
        }).ToList();
    }

    private static string PortBusyMessage(string? owner, bool heldByBpp, bool stopAttempted)
    {
        var who = owner != null ? $"program „{owner}”" : "inny program";
        if (heldByBpp && !stopAttempted)
            return $"Port {AppPaths.Port} jest zajęty przez {who} — najpewniej to działające BPP (dystrybucja WSL " +
                   "„BPP”). Kliknij „Zatrzymaj BPP”: instalator je zatrzyma i ponownie sprawdzi wymagania.";
        if (stopAttempted && Preflight.IsWslProcess(owner))
            return $"Port {AppPaths.Port} jest nadal zajęty przez {who}, mimo zatrzymania BPP — prawdopodobnie " +
                   "używa go inna dystrybucja WSL. Zatrzymaj ją (lub uruchom ponownie komputer) i uruchom " +
                   "instalator ponownie.";
        return $"Port {AppPaths.Port} jest zajęty przez {who}. Zamknij ten program i uruchom instalator ponownie. " +
               "Jeśli to działające BPP, zatrzymaj je (ikonka BPP w zasobniku → Zatrzymaj).";
    }

    private static string Gb(ulong bytes) => (bytes / (double)(1UL << 30)).ToString("0.#");

    /// <summary>
    /// Installed RAM (what the user knows as "8 GB"); GlobalMemoryStatusEx would report ~7.8 GiB on such a machine
    /// because of hardware-reserved memory and wrongly fail the 8 GiB check.
    /// </summary>
    private static ulong InstalledRamBytes()
        => GetPhysicallyInstalledSystemMemory(out var kb) ? kb * 1024 : (ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    /// <summary>
    /// Copies this exe (the one BPP.exe: tray + installer) into the program folder. Already running from there
    /// (resume after a restart, or a rerun from the Start menu folder): nothing to do.
    /// </summary>
    private void InstallProgram()
    {
        if (string.Equals(Path.GetFullPath(_setupExe), Path.GetFullPath(AppPaths.TrayExe),
                StringComparison.OrdinalIgnoreCase))
            return;
        Directory.CreateDirectory(AppPaths.ProgramDir);
        var tmp = AppPaths.TrayExe + ".new";
        try
        {
            File.Copy(_setupExe, tmp, overwrite: true);
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

    // ---- rootfs -----------------------------------------------------------------------------------------

    private static string Metadata(string key)
        => typeof(InstallerFlow).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
               .FirstOrDefault(a => a.Key == key)?.Value?.Trim() ?? "";

    /// <summary>Pinned at build time (build/build.sh); empty in development builds.</summary>
    private static string RootfsUrl => Metadata("RootfsUrl");

    private static string? RootfsSha256 => Metadata("RootfsSha256") is { Length: > 0 } h ? h : null;

    private static string RootfsAssetName
        => RootfsUrl.Length > 0 ? Uri.UnescapeDataString(RootfsUrl[(RootfsUrl.LastIndexOf('/') + 1)..]) : "bpp-rootfs-<wersja>.tar.gz";

    /// <summary>Named after the pinned hash, so a partial file of another version is never resumed.</summary>
    private static string RootfsDownloadPath
        => Path.Combine(AppPaths.DownloadDir, $"bpp-rootfs-{(RootfsSha256 ?? "dev")[..Math.Min(12, (RootfsSha256 ?? "dev").Length)]}.tar.gz");

    /// <summary>
    /// Makes <see cref="_rootfsPath"/> a verified rootfs archive: the file given with <c>--rootfs</c> (the mini
    /// installer passes one lying next to it), else the cached or freshly downloaded copy from GitHub Releases.
    /// Idempotent: after the import-failure loop it only re-checks the cached file.
    /// </summary>
    private async Task<bool> DownloadRootfsAsync()
    {
        var sha = RootfsSha256;
        if (_args.Rootfs is { } local)
        {
            _ui.SetStatus("Sprawdzanie pliku dystrybucji BPP…", null);
            if (!File.Exists(local))
                _ui.AppendLog($"Plik {local} nie istnieje — pobieram dystrybucję z internetu.");
            else if (sha != null && !await Task.Run(() => FileHash.Matches(local, sha)))
                _ui.AppendLog($"Plik {local} ma niewłaściwą sumę SHA-256 (inna wersja albo uszkodzony) — pobieram " +
                              "dystrybucję z internetu.");
            else
            {
                if (sha == null) _ui.AppendLog("Uwaga: kompilacja bez przypiętej sumy SHA-256 — plik nie jest sprawdzany.");
                _ui.AppendLog("Dystrybucja z pliku: " + local);
                _rootfsPath = local;
                return true;
            }
        }

        if (RootfsUrl.Length == 0 || sha == null)
        {
            _lastError = "Ta kompilacja instalatora nie zna adresu ani sumy kontrolnej dystrybucji BPP " +
                         "(kompilacja deweloperska). Podaj plik: BPP.exe --setup --rootfs <plik.tar.gz>.";
            return false;
        }

        _ui.SetStatus("Pobieranie dystrybucji BPP…", 0);
        var progress = new Progress<DownloadProgress>(ReportDownload); // created on the UI thread: posts there
        try
        {
            using var handler = new HttpClientHandler
            {
                UseProxy = true,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials, // authenticating proxies (NTLM)
            };
            using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"BPP-Setup/{Version}");
            _ui.AppendLog("Pobieranie: " + RootfsUrl);
            // off the UI thread: re-checking a cached ~170 MB file hashes it before the first await
            await Task.Run(() => new Downloader(http).DownloadAsync(new Uri(RootfsUrl), RootfsDownloadPath, sha,
                progress, CancellationToken.None, _ui.AppendLog));
        }
        catch (DownloadFailedException ex)
        {
            _lastError = ex.Message;
            _ui.AppendLog(ex.ToString());
            return false;
        }
        _rootfsPath = RootfsDownloadPath;
        return true;
    }

    private long _lastDownloadReport;

    private void ReportDownload(DownloadProgress p)
    {
        var now = Environment.TickCount64;
        if (now - _lastDownloadReport < 200 && p.Fraction is not 1.0) return;
        _lastDownloadReport = now;
        var text = p.Total is { } total
            ? $"Pobieranie dystrybucji BPP… {Mb(p.Received)} z {Mb(total)} MB"
            : $"Pobieranie dystrybucji BPP… {Mb(p.Received)} MB";
        _ui.SetStatus(text, p.Fraction);
        _ui.SetOverall(InstallProgress.DuringDownload(p.Fraction ?? 0));
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0");

    /// <summary>
    /// Imports the verified rootfs. Only a failing <c>wsl --import</c> returns false (the plan may then offer
    /// enable-wsl); the downloaded copy is kept until an import succeeds, so that loop does not download again.
    /// </summary>
    private async Task<bool> ImportAsync()
    {
        var tar = _rootfsPath ?? throw new InvalidOperationException("Brak pliku dystrybucji BPP do zaimportowania.");
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

        if (tar == RootfsDownloadPath) TryDelete(tar); // ~170 MB no longer needed; a --rootfs file is the user's
        await _wsl.TrySetSparseAsync();
        return true;
    }

    private async Task<InitOutcome> RunInitAsync()
    {
        using var cts = new CancellationTokenSource();
        _initCts = cts;
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
                    _ui.SetStatus(ev.Message, ev.Item); // upper bar: the image being pulled
                    _ui.SetOverall(InstallProgress.DuringInit(ev));
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

    /// <summary>The diagnostics ZIP for support (see <see cref="Diagnostics.CreateZipAsync"/>); not while the flow runs.</summary>
    public Task<string> CreateDiagnosticsZipAsync(string log)
        => Diagnostics.CreateZipAsync("instalator", log, _wsl, _failureDiag, stopDistroIfStarted: true);

    private static string Version
        => typeof(InstallerFlow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>
    /// Best effort: a killed or crashed run of an older installer may have left its ~1 GB rootfs in %TEMP%, and
    /// older mini-installer downloads of BPP.exe may sit in the download folder. Only one Setup runs at a time
    /// (mutex), so every match is stale (except this very exe); a file still in use simply stays.
    /// </summary>
    private void SweepStaleFiles()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), RootfsTempPrefix + "*.tar.gz"))
            {
                _ui.AppendLog("Usuwanie pozostałości poprzedniej instalacji: " + f);
                TryDelete(f);
            }
            if (Directory.Exists(AppPaths.DownloadDir))
                foreach (var f in Directory.EnumerateFiles(AppPaths.DownloadDir))
                {
                    var name = Path.GetFileName(f);
                    var staleApp = (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                    name.EndsWith(".exe" + Downloader.PartSuffix, StringComparison.OrdinalIgnoreCase)) &&
                                   !string.Equals(Path.GetFullPath(f), Path.GetFullPath(_setupExe), StringComparison.OrdinalIgnoreCase);
                    var staleRootfs = name.StartsWith("bpp-rootfs-", StringComparison.OrdinalIgnoreCase) &&
                                      !name.StartsWith(Path.GetFileName(RootfsDownloadPath), StringComparison.OrdinalIgnoreCase);
                    if (staleApp || staleRootfs) TryDelete(f);
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ui.AppendLog("Sprzątanie plików tymczasowych: " + ex.Message);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
}
