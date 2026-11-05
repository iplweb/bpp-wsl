using BPP.Core;

namespace BPP.Setup;

/// <summary>
/// <c>BPP.exe --setup [--resume] [--install-anyway] [--rootfs &lt;file&gt;]</c>; BPP.exe runs asInvoker, UAC only for
/// System32 dism.exe / netsh.exe (Ruling R8). The caller has already run <c>ApplicationConfiguration.Initialize</c>.
/// </summary>
public static class SetupEntry
{
    private const string MutexName = @"Local\BPP.Setup";

    public static int Run(AppArgs args)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Instalator BPP jest już uruchomiony.", "BPP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        using (var form = new SetupForm(args))
            Application.Run(form);
        GC.KeepAlive(mutex);
        return 0;
    }
}
