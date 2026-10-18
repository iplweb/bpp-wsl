using System.Runtime.InteropServices;

namespace BPP.Core;

public record PreflightInput(ulong TotalRamBytes, long FreeDiskBytes, Architecture Arch, bool Port8443Free);

public enum PreflightProblem { NotX64, TooLittleRam, TooLittleDisk, PortBusy }

public static class Preflight
{
    public const ulong MinRam = 8UL << 30;
    public const long MinDisk = 25L << 30;

    public static IReadOnlyList<PreflightProblem> Check(PreflightInput input)
    {
        var problems = new List<PreflightProblem>();
        if (input.Arch != Architecture.X64) problems.Add(PreflightProblem.NotX64);
        if (input.TotalRamBytes < MinRam) problems.Add(PreflightProblem.TooLittleRam);
        if (input.FreeDiskBytes < MinDisk) problems.Add(PreflightProblem.TooLittleDisk);
        if (!input.Port8443Free) problems.Add(PreflightProblem.PortBusy);
        return problems;
    }

    /// <summary>
    /// Windows-side processes that hold a port on behalf of a WSL distro (wslrelay forwards localhost for
    /// WSL2 distros; the others cover other WSL builds / networking modes).
    /// </summary>
    private static readonly HashSet<string> WslProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "wslrelay", "wslhost", "wslservice", "wsl", "vmmem", "vmmemWSL",
    };

    public static bool IsWslProcess(string? processName) => processName != null && WslProcesses.Contains(processName);

    /// <summary>
    /// The busy port is most likely the BPP distro left running without the tray: 8443 is the only problem, it is
    /// held through WSL and the BPP distro exists. Only a guess (wslrelay serves every WSL2 distro), so the installer
    /// offers "Zatrzymaj BPP" once and re-checks.
    /// </summary>
    public static bool PortHeldByBpp(IReadOnlyList<PreflightProblem> problems, string? portOwner, bool bppDistroExists)
        => problems is [PreflightProblem.PortBusy] && IsWslProcess(portOwner) && bppDistroExists;
}
