using System.Diagnostics;
using BPP.Windows;

namespace BPP.Tray;

internal static class Program
{
    public const string Url = "https://localhost:8443/";
    private const string MutexName = @"Local\BPP.Tray";

    [STAThread]
    private static int Main(string[] args)
    {
        // BPP.exe is never elevated (Ruling R8): UAC runs only System32 dism.exe / netsh.exe.
        // "--uninstall" is dispatched before the single-instance mutex: Settings launches it while the tray runs.
        if (args.Length > 0 && args is not ["--uninstall"] and not ["--tray"])
            return 2;

        ApplicationConfiguration.Initialize();

        if (args is ["--uninstall"])
            return Uninstaller.RunStandalone();

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            OpenBrowser();
            return 0;
        }

        bool removeProgramDir;
        using (var context = new TrayContext())
        {
            Application.Run(context);
            removeProgramDir = context.ProgramDirRemovalRequested;
        }
        GC.KeepAlive(mutex);
        if (removeProgramDir) Uninstaller.ScheduleProgramDirRemoval(); // last action before exit
        return 0;
    }

    public static void OpenBrowser()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show($"Nie udało się otworzyć przeglądarki. Otwórz ręcznie adres {Url}",
                "BPP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
