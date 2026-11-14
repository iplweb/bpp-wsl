using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BPP.Core;

/// <summary>
/// The BPP distro's local certificate authority (<c>lib/cert.sh</c>, <c>bpp-ctl ca-cert</c>). The Windows app adds
/// it to the current user's trusted roots so the browser opens https://localhost:8443 without a warning.
/// Before that the certificate is checked here: whatever the distro hands over, the only thing ever offered to
/// the Windows root store is a CA that cannot vouch for a real domain.
/// </summary>
public static class LocalCa
{
    /// <summary>Common name of the CA (<c>BPP_CA_SUBJECT</c> in cert.sh); also how its store entries are found.</summary>
    public const string CommonName = "BPP localhost CA";

    private const string NameConstraintsOid = "2.5.29.30";
    private static readonly Asn1Tag PermittedSubtrees = new(TagClass.ContextSpecific, 0, isConstructed: true);
    private static readonly Asn1Tag DnsName = new(TagClass.ContextSpecific, 2);
    private static readonly Asn1Tag IpAddress = new(TagClass.ContextSpecific, 7);

    /// <summary>Parses <c>bpp-ctl ca-cert</c> output. Throws <see cref="CryptographicException"/> when it is not a certificate.</summary>
    public static X509Certificate2 Parse(string pem) => X509Certificate2.CreateFromPem(pem);

    /// <summary>True for a certificate named like the BPP CA (self-signed, <see cref="CommonName"/>).</summary>
    public static bool IsNamedLikeBppCa(X509Certificate2 cert)
        => cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false) == CommonName
           && cert.SubjectName.RawData.AsSpan().SequenceEqual(cert.IssuerName.RawData);

    /// <summary>
    /// Why <paramref name="cert"/> must not be trusted, or null when it is the constrained BPP CA: self-signed with
    /// the expected name, a CA, and with critical name constraints that permit no DNS name other than localhost.
    /// </summary>
    public static string? Problem(X509Certificate2 cert)
    {
        if (!IsNamedLikeBppCa(cert)) return $"to nie jest certyfikat „{CommonName}”";
        if (cert.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault() is not { CertificateAuthority: true })
            return "certyfikat nie jest urzędem certyfikacji";
        if (cert.Extensions[NameConstraintsOid] is not { Critical: true } constraints)
            return "certyfikat nie ma krytycznego ograniczenia nazw";

        try
        {
            var extension = new AsnReader(constraints.RawData, AsnEncodingRules.DER).ReadSequence();
            if (!extension.HasData || extension.PeekTag() != PermittedSubtrees)
                return "ograniczenie nazw nie zawiera listy dozwolonych nazw";

            var subtrees = extension.ReadSequence(PermittedSubtrees);
            var dnsNames = 0;
            while (subtrees.HasData)
            {
                var subtree = subtrees.ReadSequence();
                var tag = subtree.PeekTag();
                if (tag == DnsName)
                {
                    var name = subtree.ReadCharacterString(UniversalTagNumber.IA5String, DnsName);
                    if (name != "localhost") return $"certyfikat dopuszcza nazwę „{name}”";
                    dnsNames++;
                }
                else if (tag != IpAddress)
                {
                    return "ograniczenie nazw zawiera nieoczekiwany rodzaj nazwy";
                }
            }
            // without a DNS entry among the permitted names, DNS names would not be constrained at all
            return dnsNames > 0 ? null : "ograniczenie nazw nie obejmuje nazw DNS";
        }
        catch (AsnContentException)
        {
            return "nieczytelne ograniczenie nazw";
        }
    }
}
