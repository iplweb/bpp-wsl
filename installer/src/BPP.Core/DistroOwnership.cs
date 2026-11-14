namespace BPP.Core;

/// <summary>Whose WSL distro carries our name: nobody's (not registered), ours, or somebody else's.</summary>
public enum DistroOwner { None, Ours, Foreign }

/// <summary>
/// WSL identifies a distro by name only, and other distros (Ubuntu, Fedora, …) live next to ours. A distro named
/// like ours counts as ours only when WSL keeps it in our data directory; one kept anywhere else is the user's
/// own and is never stopped, unregistered or replaced.
/// </summary>
public static class DistroOwnership
{
    /// <param name="listed">The name is in <c>wsl -l</c>.</param>
    /// <param name="registeredDir">
    /// Where WSL keeps that distro (its <c>BasePath</c>), or null when that could not be read. Unknown counts as
    /// ours: only a distro positively found elsewhere is foreign, so an unreadable registry never blocks an
    /// uninstall or a reinstall of a real BPP.
    /// </param>
    /// <param name="ourDir">The directory our installer imports into.</param>
    public static DistroOwner Classify(bool listed, string? registeredDir, string ourDir)
    {
        if (!listed) return DistroOwner.None;
        return registeredDir == null || SameDirectory(registeredDir, ourDir) ? DistroOwner.Ours : DistroOwner.Foreign;
    }

    /// <summary>
    /// Compares two Windows directory paths the way WSL may spell them: with or without the <c>\\?\</c> prefix
    /// or a trailing separator, either slash, any letter case.
    /// </summary>
    public static bool SameDirectory(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
    {
        var p = path.Trim().Replace('/', '\\');
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p[4..];
        return p.TrimEnd('\\');
    }
}
