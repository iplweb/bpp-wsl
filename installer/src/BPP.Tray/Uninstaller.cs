using System.Diagnostics;
using BPP.Core;
using BPP.Windows;

namespace BPP.Tray;

/// <summary>Outcome of an uninstall: what <see cref="UninstallPlan"/> allowed to remove, and what went wrong.</summary>
internal sealed record UninstallReport(UninstallCleanup Cleanup, IReadOnlyList<string> Problems);

/// <summary>
/// <c>--uninstall</c>, as the ordinary user: stops a running stack and unregisters the distro; only once the distro
/// is really gone (<see cref="UninstallPlan"/>) removes per-user files and registration, asks UAC once to remove the
/// firewall rule, then deletes the program directory after exit. If the distro is still there, nothing else is
/// touched and the user is asked to retry. Cleanup steps tolerate a partial install; failures are shown at the end.
/// </summary>
internal static class Uninstaller
{
    private const string Caption = "BPP — odinstalowanie";
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Entry point for <c>BPP.exe --uninstall</c> (e.g. from Settings → Aplikacje).</summary>
    public static int RunStandalone()
    {
        if (!Confirm()) return 1;

        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        using var context = new ApplicationContext();
        UninstallReport? report = null;
        _ = RunAndExitAsync();
        Application.Run(context);
        // unexpected failure or the distro is still there: keep the program so the user can retry
        if (report is not { Cleanup.RemoveProgramDir: true }) return 1;
        ScheduleProgramDirRemoval();
        return report.Problems.Count == 0 ? 0 : 1;

        async Task RunAndExitAsync()
        {
            try
            {
                report = await RunWithProgressAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Odinstalowanie przerwane: " + ex.Message, Caption,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                context.ExitThread();
            }
        }
    }

    public static bool Confirm()
        => MessageBox.Show(
            "Odinstalować BPP?\n\nZostaną usunięte program, dystrybucja WSL „BPP” i wszystkie dane testowe.",
            Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
           == DialogResult.Yes;

    /// <summary>
    /// Shows a progress window, uninstalls, then shows the summary. The caller removes the program directory only
    /// when <see cref="UninstallCleanup.RemoveProgramDir"/> is set.
    /// </summary>
    public static async Task<UninstallReport> RunWithProgressAsync()
    {
        UninstallReport report;
        using (var progress = CreateProgressForm())
        {
            progress.Show();
            report = await UninstallAsync();
            progress.Close();
        }

        if (!report.Cleanup.RemoveProgramDir)
        {
            MessageBox.Show("Nie udało się odinstalować BPP:\n\n• " + string.Join("\n• ", report.Problems) +
                            "\n\nProgram, dane i skróty pozostały bez zmian. Spróbuj ponownie odinstalować " +
                            "(Ustawienia → Aplikacje → BPP albo menu ikonki BPP → Odinstaluj), w razie potrzeby " +
                            "po ponownym uruchomieniu komputera.",
                Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else if (report.Problems.Count == 0)
        {
            MessageBox.Show("BPP zostało odinstalowane.", Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show("BPP zostało odinstalowane, ale nie wszystko się udało:\n\n• " +
                            string.Join("\n• ", report.Problems),
                Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return report;
    }

    private static async Task<UninstallReport> UninstallAsync()
    {
        var problems = new List<string>();

        await Task.Run(StopOtherInstances);

        var wsl = new WslClient();
        var existed = await wsl.DistroExistsAsync();
        var unregistered = false;
        if (existed)
        {
            if (UninstallPlan.ShouldStopBeforeUnregister(existed, await wsl.IsDistroRunningAsync()))
            {
                using var timeout = new CancellationTokenSource(StopTimeout);
                try { await wsl.RunCtlTextAsync("stop", timeout.Token); }
                catch (Exception) { /* the distro is being removed anyway */ }
            }

            try
            {
                await wsl.UnregisterAsync();
                unregistered = true;
            }
            catch (Exception ex) { problems.Add("Nie udało się usunąć dystrybucji WSL „BPP”: " + ex.Message); }
        }

        var listed = existed && await wsl.DistroExistsAsync();
        if (unregistered && listed) problems.Add("Dystrybucja WSL „BPP” nadal jest zarejestrowana.");
        // the distro still registered: its VHD, the Apps entry and BPP.exe must stay so the uninstall can be retried
        var cleanup = UninstallPlan.AfterUnregister(UninstallPlan.DistroGone(existed, unregistered, listed));

        if (cleanup.RemoveData)
        {
            var dataDir = Path.GetDirectoryName(AppPaths.DistroDir)!; // %LOCALAPPDATA%\BPP
            try
            {
                if (Directory.Exists(dataDir)) await Task.Run(() => Directory.Delete(dataDir, recursive: true));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"Nie udało się usunąć katalogu {dataDir}: {ex.Message}");
            }
        }

        if (cleanup.RemoveRegistration)
        {
            var registration = new Registration();
            Step(problems, "skrótu w Menu Start", registration.RemoveStartMenuShortcut);
            Step(problems, "wpisu w „Aplikacje”", registration.UnregisterUninstall);
            Step(problems, "wpisu RunOnce", registration.ClearRunOnce);
        }

        if (cleanup.RemoveFirewallRule) await RemoveFirewallRuleAsync(problems);

        return new UninstallReport(cleanup, problems);
    }

    private static async Task RemoveFirewallRuleAsync(List<string> problems)
    {
        try
        {
            // RemoveElevated checks RuleExists unelevated first and prompts only when the rule is there
            var code = await Task.Run(() => new Firewall().RemoveElevated());
            if (code == null)
                problems.Add($"Reguła zapory „{AppPaths.FirewallRuleName}” została (brak zgody administratora). " +
                             "Jest nieszkodliwa; administrator może ją usunąć w Zaporze Windows.");
            else if (code != 0)
                problems.Add($"Nie udało się usunąć reguły zapory „{AppPaths.FirewallRuleName}” (netsh: kod {code}).");
        }
        catch (Exception ex)
        {
            problems.Add($"Nie udało się usunąć reguły zapory „{AppPaths.FirewallRuleName}”: {ex.Message}");
        }
    }

    private static void Step(List<string> problems, string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { problems.Add($"Nie udało się usunąć {what}: {ex.Message}"); }
    }

    /// <summary>Ends a running tray (it holds BPP.exe open and would restart the distro on its own).</summary>
    private static void StopOtherInstances()
    {
        var self = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("BPP"))
        {
            using (p)
            {
                try
                {
                    if (p.Id == self) continue;
                    var file = p.MainModule?.FileName;
                    if (file == null || !string.Equals(Path.GetDirectoryName(file), AppPaths.ProgramDir,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // exited meanwhile or not ours to inspect
                }
            }
        }
    }

    /// <summary>
    /// The running BPP.exe cannot delete itself: a detached cmd waits for this process to exit, then removes
    /// the program directory. The directory travels in an environment variable, so characters such as
    /// <c>&amp;</c>, <c>%</c> or <c>^</c> in the user profile path are never parsed by cmd.
    /// Must be the very last action before the process exits.
    /// </summary>
    public static void ScheduleProgramDirRemoval()
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            // ping, not timeout: timeout exits at once when there is no console input to wait on
            Arguments = "/d /c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"%BPP_PROGRAM_DIR%\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.SystemDirectory, // never inside the directory being removed
        };
        psi.Environment["BPP_PROGRAM_DIR"] = AppPaths.ProgramDir;
        try
        {
            using var _ = Process.Start(psi);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // leaves only the program files behind; nothing else to do this late
        }
    }

    private static Form CreateProgressForm()
    {
        var form = new Form
        {
            Text = Caption,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ControlBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(380, 90),
            ShowInTaskbar = true,
        };
        form.Controls.Add(new Label
        {
            Text = "Odinstalowywanie BPP… To może potrwać do minuty.",
            AutoSize = true,
            Location = new Point(16, 16),
        });
        form.Controls.Add(new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            Location = new Point(16, 48),
            Size = new Size(348, 20),
        });
        return form;
    }
}
