using System.Diagnostics;
using BPP.Core;
using BPP.Setup;
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
        // One exe for the tray and the installer: the mini BPP-Setup.exe downloads it and runs "--setup".
        // "--uninstall" and "--setup" are dispatched before the tray's single-instance mutex.
        var parsed = AppArgs.Parse(args);
        if (parsed == null) return 2;

        ApplicationConfiguration.Initialize();

        switch (parsed.Mode)
        {
            case AppMode.Uninstall:
                return Uninstaller.RunStandalone();
            case AppMode.Setup:
                return SetupEntry.Run(parsed);
        }

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
