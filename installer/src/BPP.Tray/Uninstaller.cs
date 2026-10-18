using System.Diagnostics;
using BPP.Windows;

namespace BPP.Tray;

/// <summary>
/// <c>--uninstall</c>, as the ordinary user: stops and unregisters the distro, removes per-user files and
/// registration, asks UAC once to remove the firewall rule, then deletes the program directory after exit.
/// Every step is independent and tolerates a partial install; failures are collected and shown at the end.
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
        int? code = null;
        _ = RunAndExitAsync();
        Application.Run(context);
        if (code == null) return 1; // unexpected failure: keep the program so the user can retry
        ScheduleProgramDirRemoval();
        return code.Value;

        async Task RunAndExitAsync()
        {
            try
            {
                code = await RunWithProgressAsync() ? 0 : 1;
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

    /// <summary>Shows a progress window, uninstalls, then shows the summary. True when nothing failed.</summary>
    public static async Task<bool> RunWithProgressAsync()
    {
        List<string> problems;
        using (var progress = CreateProgressForm())
        {
            progress.Show();
            problems = await UninstallAsync();
            progress.Close();
        }

        if (problems.Count == 0)
        {
            MessageBox.Show("BPP zostało odinstalowane.", Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        MessageBox.Show("BPP zostało odinstalowane, ale nie wszystko się udało:\n\n• " + string.Join("\n• ", problems),
            Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private static async Task<List<string>> UninstallAsync()
    {
        var problems = new List<string>();

        await Task.Run(StopOtherInstances);

        var wsl = new WslClient();
        if (await wsl.DistroExistsAsync())
        {
            using (var timeout = new CancellationTokenSource(StopTimeout))
            {
                try { await wsl.RunCtlTextAsync("stop", timeout.Token); }
                catch (Exception) { /* the distro is being removed anyway */ }
            }

            try { await wsl.UnregisterAsync(); }
            catch (Exception ex) { problems.Add("Nie udało się usunąć dystrybucji WSL „BPP”: " + ex.Message); }
        }

        var dataDir = Path.GetDirectoryName(AppPaths.DistroDir)!; // %LOCALAPPDATA%\BPP
        try
        {
            if (Directory.Exists(dataDir)) await Task.Run(() => Directory.Delete(dataDir, recursive: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"Nie udało się usunąć katalogu {dataDir}: {ex.Message}");
        }

        var registration = new Registration();
        Step(problems, "skrótu w Menu Start", registration.RemoveStartMenuShortcut);
        Step(problems, "wpisu w „Aplikacje”", registration.UnregisterUninstall);
        Step(problems, "wpisu RunOnce", registration.ClearRunOnce);

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

        return problems;
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
