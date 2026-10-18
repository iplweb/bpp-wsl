using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BPP.Core;

/// <summary>
/// Per-user name of the inbound firewall rule: <c>BPP (8443) &lt;8 hex&gt;</c>, the hex being the first 8 lowercase
/// digits of SHA-256 of the user's SID string. Firewall rules are machine-wide but BPP is installed per user, so a
/// shared name would let one account's uninstall delete (or its install keep) another account's rule.
/// </summary>
public static class FirewallRuleName
{
    public const string Prefix = "BPP (8443) ";

    private static readonly Regex Valid = new(@"^BPP \(8443\) [0-9a-f]{8}$", RegexOptions.Compiled);

    /// <summary>Rule name for <paramref name="userSid"/> (e.g. <c>WindowsIdentity.GetCurrent().User.Value</c>).</summary>
    public static string ForUser(string userSid)
    {
        if (string.IsNullOrWhiteSpace(userSid))
            throw new ArgumentException("Wymagany identyfikator SID użytkownika.", nameof(userSid));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userSid));
        return Prefix + Convert.ToHexStringLower(hash)[..8];
    }

    /// <summary>Only names produced by <see cref="ForUser"/> may reach the elevated netsh command line.</summary>
    public static bool IsValid(string name) => Valid.IsMatch(name);
}
