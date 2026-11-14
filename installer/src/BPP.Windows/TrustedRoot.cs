using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BPP.Core;

namespace BPP.Windows;

public enum TrustOutcome { AlreadyTrusted, Added, Declined }

/// <summary>
/// The BPP local CA (<see cref="LocalCa"/>) in the current user's "Trusted Root Certification Authorities" store —
/// what Edge and Chrome consult. Per user, like the rest of BPP, so nothing is elevated; instead Windows itself
/// asks the user to confirm every addition to and removal from this store with a security dialog.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TrustedRoot
{
    /// <summary>Reads the CA from the distro (<c>bpp-ctl ca-cert</c>). Throws when it is not the constrained BPP CA.</summary>
    public static async Task<X509Certificate2> ReadFromDistroAsync(WslClient wsl, CancellationToken ct = default)
    {
        var pem = await wsl.RunCtlTextAsync("ca-cert", ct).ConfigureAwait(false);
        X509Certificate2 ca;
        try { ca = LocalCa.Parse(pem); }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("bpp-ctl ca-cert nie zwrócił certyfikatu: " + ex.Message, ex);
        }
        if (LocalCa.Problem(ca) is { } problem)
        {
            ca.Dispose();
            throw new InvalidOperationException("Certyfikat CA z dystrybucji BPP został odrzucony: " + problem + ".");
        }
        return ca;
    }

    public static bool IsTrusted(X509Certificate2 ca)
    {
        using var store = Open(OpenFlags.ReadOnly);
        return Contains(store, ca);
    }

    /// <summary>
    /// Adds <paramref name="ca"/> (Windows shows its "Security Warning" dialog) and then drops BPP CAs left by
    /// earlier installs, whose keys no longer exist. <see cref="TrustOutcome.Declined"/> when the user said no.
    /// </summary>
    public static TrustOutcome Trust(X509Certificate2 ca)
    {
        if (LocalCa.Problem(ca) is { } problem) throw new ArgumentException(problem, nameof(ca));

        using var store = Open(OpenFlags.ReadWrite);
        var outcome = TrustOutcome.AlreadyTrusted;
        if (!Contains(store, ca))
        {
            try { store.Add(ca); }
            catch (CryptographicException) when (!Contains(store, ca))
            {
                return TrustOutcome.Declined; // "No" in the Windows dialog
            }
            outcome = TrustOutcome.Added;
        }
        RemoveWhere(store, c => c.Thumbprint != ca.Thumbprint);
        return outcome;
    }

    /// <summary>Removes every BPP CA from the store (uninstall). False when one stayed: the user declined the Windows dialog.</summary>
    public static bool RemoveAll()
    {
        using var store = Open(OpenFlags.ReadWrite);
        return RemoveWhere(store, _ => true);
    }

    private static X509Store Open(OpenFlags flags)
    {
        var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(flags);
        return store;
    }

    private static bool Contains(X509Store store, X509Certificate2 ca)
    {
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, ca.Thumbprint, validOnly: false);
        try { return found.Count > 0; }
        finally { DisposeAll(found); }
    }

    /// <summary>Removes the BPP CAs matching <paramref name="match"/>; false when any of them is still there.</summary>
    private static bool RemoveWhere(X509Store store, Func<X509Certificate2, bool> match)
    {
        var all = store.Certificates;
        try
        {
            var removedAll = true;
            foreach (var cert in all.Where(c => LocalCa.IsNamedLikeBppCa(c) && match(c)))
            {
                try { store.Remove(cert); }
                catch (CryptographicException) when (Contains(store, cert))
                {
                    removedAll = false; // "No" in the Windows dialog
                }
            }
            return removedAll;
        }
        finally { DisposeAll(all); }
    }

    private static void DisposeAll(X509Certificate2Collection certs)
    {
        foreach (var c in certs) c.Dispose();
    }
}
