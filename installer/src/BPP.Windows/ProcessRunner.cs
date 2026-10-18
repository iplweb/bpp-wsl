using System.Diagnostics;
using BPP.Core;

namespace BPP.Windows;

internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Runs a child process with ArgumentList, no shell, no window; stdout and stderr drained concurrently.</summary>
internal static class ProcessRunner
{
    public static ProcessStartInfo CreateStartInfo(string file, IEnumerable<string> args, bool utf8Env)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (utf8Env) psi.Environment["WSL_UTF8"] = "1";
        return psi;
    }

    public static async Task<ProcessResult> RunAsync(
        string file, IEnumerable<string> args, bool utf8Env = false, CancellationToken ct = default)
    {
        using var p = Process.Start(CreateStartInfo(file, args, utf8Env))
            ?? throw new InvalidOperationException($"Nie można uruchomić {file}.");
        using var reg = ct.Register(() => TryKill(p));

        var outTask = ReadAllAsync(p.StandardOutput.BaseStream);
        var errTask = ReadAllAsync(p.StandardError.BaseStream);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        return new ProcessResult(p.ExitCode, WslText.Decode(await outTask), WslText.Decode(await errTask));
    }

    public static ProcessResult Run(string file, IEnumerable<string> args)
        => RunAsync(file, args).GetAwaiter().GetResult();

    public static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static async Task<byte[]> ReadAllAsync(Stream s)
    {
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }
}
