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
}
