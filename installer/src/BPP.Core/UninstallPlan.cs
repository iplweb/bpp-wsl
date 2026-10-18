namespace BPP.Core;

/// <summary>What the uninstaller removes after the distro step (everything or nothing).</summary>
public sealed record UninstallCleanup(bool RemoveData, bool RemoveRegistration, bool RemoveFirewallRule, bool RemoveProgramDir);

/// <summary>
/// Decisions of <c>BPP.exe --uninstall</c>. The distro is removed first; only once it is really gone are the data
/// directory (it holds the distro's VHD), shortcut, Apps entry, firewall rule and program directory removed.
/// Otherwise everything stays, so the user can simply run the uninstall again.
/// </summary>
public static class UninstallPlan
{
    private static readonly UninstallCleanup Everything = new(true, true, true, true);
    private static readonly UninstallCleanup Nothing = new(false, false, false, false);

    /// <summary>`bpp-ctl stop` only for a running distro: on a stopped one it would just boot the VM.</summary>
    public static bool ShouldStopBeforeUnregister(bool distroExists, bool distroRunning) => distroExists && distroRunning;

    /// <summary>
    /// Gone when it was never registered, or <c>wsl --unregister</c> succeeded and it is no longer listed.
    /// A failed unregister is not trusted even with an empty listing (the listing itself may have failed).
    /// </summary>
    public static bool DistroGone(bool existedBefore, bool unregisterSucceeded, bool listedAfterwards)
        => !listedAfterwards && (!existedBefore || unregisterSucceeded);

    public static UninstallCleanup AfterUnregister(bool distroGone) => distroGone ? Everything : Nothing;
}
