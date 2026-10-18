namespace BPP.Setup;

internal static class Program
{
    public const string ResumeArg = "--resume";
    private const string MutexName = @"Local\BPP.Setup";

    /// <summary><c>BPP-Setup.exe [--resume]</c>; runs asInvoker, UAC only for System32 dism.exe / netsh.exe (Ruling R8).</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is not [] and not [ResumeArg]) return 2;

        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Instalator BPP jest już uruchomiony.", "BPP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        using (var form = new SetupForm(resume: args is [ResumeArg]))
            Application.Run(form);
        GC.KeepAlive(mutex);
        return 0;
    }
}
