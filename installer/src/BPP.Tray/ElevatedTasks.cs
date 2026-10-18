using BPP.Core;
using BPP.Windows;

namespace BPP.Tray;

/// <summary>
/// <c>BPP.exe --elevated &lt;task&gt; [args]</c>: the short-lived UAC child. Does exactly one system-wide task
/// and exits with its code, without UI. It may run under another account (over-the-shoulder UAC), so it
/// never touches per-user state (HKCU, %LOCALAPPDATA%, AppPaths-derived paths): paths come as arguments.
/// </summary>
internal static class ElevatedTasks
{
    private const int InvalidArguments = 2;
    private const int Failed = 1;

    public static int Run(string[] args)
    {
        var request = ElevatedArgs.Parse(args);
        if (request == null) return InvalidArguments;

        try
        {
            switch (request.Task)
            {
                case ElevatedArgs.EnableWsl:
                    // 0, 3010 (restart required) or the failing DISM exit code, passed through as-is
                    return new WindowsFeatures().EnableWslFeatures();
                case ElevatedArgs.Firewall:
                    new Firewall().Add(request.Args[0]);
                    return 0;
                case ElevatedArgs.RemoveFirewall:
                    new Firewall().Remove();
                    return 0;
                default:
                    return InvalidArguments;
            }
        }
        catch (Exception)
        {
            // an unhandled exception in a WinExe would show a WER dialog; the caller only needs the code
            return Failed;
        }
    }
}
