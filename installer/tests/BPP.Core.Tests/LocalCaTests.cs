using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BPP.Core;

namespace BPP.Core.Tests;

public class LocalCaTests
{
    /// <summary>A CA made by the distro's <c>cert_ca_ensure</c> (lib/cert.sh): the contract between the two sides.</summary>
    private const string DistroCa = """
        -----BEGIN CERTIFICATE-----
        MIIDZTCCAk2gAwIBAgIUOQ0I92sCjypE6TlWE+Jt3hnvCOAwDQYJKoZIhvcNAQEL
        BQAwKTEMMAoGA1UECgwDQlBQMRkwFwYDVQQDDBBCUFAgbG9jYWxob3N0IENBMB4X
        DTI2MTExNDIwMDMwMVoXDTM2MTExMTIwMDMwMVowKTEMMAoGA1UECgwDQlBQMRkw
        FwYDVQQDDBBCUFAgbG9jYWxob3N0IENBMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8A
        MIIBCgKCAQEAzw04/fshulcBO5VNw4ORhUwOyfYLjm2/KA5HMK7qEChAVLKhf/dM
        gtCRbIwtmuVXIBbBbLQwX2nE/phWr1BFxR28CwweTSzeLBfC32h8kGLcZumwFBNM
        Vgg8en+A00kH5CPMlP6z2yDWkuU/soeNF+68kPRa5wNHTJ8hfheD/Eqfb3oEZSEv
        OPetRZIethN+nrHTPvyMHsLj+6pODkl3WyxHBPlOcwbUgLGbDgT32jjwnPztsasB
        0G20QodWkLLY6/XJGgWPGmtgq8BLypvdDYXJA1LbFZdl6r0Yq6ad+aIDzP7j6j75
        q67F9yeeV0jof1MBDfunHK3wgEmOyUuhdwIDAQABo4GEMIGBMBIGA1UdEwEB/wQI
        MAYBAf8CAQAwDgYDVR0PAQH/BAQDAgEGMBMGA1UdJQQMMAoGCCsGAQUFBwMBMB0G
        A1UdDgQWBBS/+oqixs0obzRQE00P/qaEkzuE/TAnBgNVHR4BAf8EHTAboBkwC4IJ
        bG9jYWxob3N0MAqHCAAAAAAAAAAAMA0GCSqGSIb3DQEBCwUAA4IBAQBKzDzN8WB0
        MtRTUlcslQ/AJOouSI1wAepn2uFXa3aqcCYTiLYmwb5KafjpYOW8Tn8BLkznWZwW
        eIKvtRtMX+I5tT6vjkxZ8fRcYbcOQZr1CHxSZcsjKvaIKzCl110axM8H4+e0yJ6f
        8dwMoYvazG9nK3u5KMZIQOQ8qwZ3fCitYoitMWkUIB3EkVirjt76kinjp77rvXDS
        OyccgRJXtbAbuOTrGWZeWDxiID8efLMMDpSt0fBR11h7Y8lknFT3O3b+u45N9g+j
        V4s3OYx/NjezZWCMMR+CAtdE/ooVEUI8ndaWvo8MW74Z8thtG5/eyElBA/0Msq4I
        WWmf3UTWI1Lt
        -----END CERTIFICATE-----
        """;

    /// <summary>Same name, made with plain openssl: no name constraints.</summary>
    private const string Unconstrained = """
        -----BEGIN CERTIFICATE-----
        MIIDMzCCAhugAwIBAgIUMhiGUw0pHKV4C+2L2UTooXH8hfMwDQYJKoZIhvcNAQEL
        BQAwKTEMMAoGA1UECgwDQlBQMRkwFwYDVQQDDBBCUFAgbG9jYWxob3N0IENBMB4X
        DTI2MTExNDIwMDMwMVoXDTM2MTExMTIwMDMwMVowKTEMMAoGA1UECgwDQlBQMRkw
        FwYDVQQDDBBCUFAgbG9jYWxob3N0IENBMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8A
        MIIBCgKCAQEAzbvvhdN/uACkSN9CKW5kZBZqQ16FG/CBlrSwpyszKzLvYtbQHKIX
        0Lqpa2CLMKs+UNZL6xfM3aiWCUbHf0gCVY/gwruVjjEAElNCyjjL3k8YKIplThLw
        sVh7Fbhz47rgW+t9HI3hM7naOlWmOpxEzHnexAsuf++S3ovq3x0rDnE/pb79t4KO
        fH4nRLbmPbfnU9ZGlu+7fLc8aL6kdXGYg37RUOi/biyAVGFksItkxTQhA5cVI3iN
        nvSnDbSmXFi9ZsU631YPRlPsNSbYJT2E75ugx4bGaRnap9Z6j5muqkTOgqOEXCl4
        yfkwUe/ksUgJ3+bfrlxpb0kfZTd+oFpThQIDAQABo1MwUTAdBgNVHQ4EFgQUVjm/
        iFMZJbjmt8I3PhKXibuBRGIwHwYDVR0jBBgwFoAUVjm/iFMZJbjmt8I3PhKXibuB
        RGIwDwYDVR0TAQH/BAUwAwEB/zANBgkqhkiG9w0BAQsFAAOCAQEAOuowIl4Qe7a5
        rPLwwieAdkdzoQuUeJgx6quWXw+uCqKKHol5L2JS/PuirwXLyvjf3OU6EuCUCeL8
        DHv9HNxxqdLhj3JCeYOzsLMqomDQWMAPrep3dEidiOpFQVsxNoCJU99z0IhB3kN0
        KX/lhnobde8asFDrNKQC+MCmtt8XwRgdLdn7yEzh5K4PCSg3FnSPRpM+qJ1xxfKb
        7EmiCPwtVXAPtwqGFveILX5op5dci8JMi6SJiHIo57jTiDKgaQRQUpnvISNY+/ab
        bYiuddZRk0oJ1rHMsp7MEe+hmyn3PAN5g/+Spp7mrcMXBkSYsXlaBqLcR/TP95zt
        Xv8rcmm3yA==
        -----END CERTIFICATE-----
        """;

    [Fact]
    public void Ca_generated_by_the_distro_is_accepted()
    {
        using var ca = LocalCa.Parse(DistroCa);
        Assert.Null(LocalCa.Problem(ca));
        Assert.True(LocalCa.IsNamedLikeBppCa(ca));
    }

    [Fact]
    public void Ca_without_name_constraints_is_rejected()
    {
        using var ca = LocalCa.Parse(Unconstrained);
        Assert.True(LocalCa.IsNamedLikeBppCa(ca));
        Assert.Contains("ograniczenia nazw", LocalCa.Problem(ca));
    }

    [Fact]
    public void Garbage_is_not_a_certificate()
        => Assert.ThrowsAny<CryptographicException>(() => LocalCa.Parse("-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----"));

    [Theory]
    [InlineData(new[] { "localhost" }, true, true, null)]
    [InlineData(new[] { "localhost", "example.com" }, true, true, "example.com")]
    [InlineData(new[] { "com" }, true, true, "„com”")]
    [InlineData(new string[0], true, true, "nie obejmuje nazw DNS")] // IP only: DNS names would be unconstrained
    [InlineData(new[] { "localhost" }, false, true, "krytycznego")]
    [InlineData(new[] { "localhost" }, true, false, "nie jest urzędem")]
    public void Only_a_critical_localhost_constraint_on_a_ca_passes(string[] dns, bool critical, bool isCa, string? problem)
    {
        using var ca = Make(LocalCa.CommonName, isCa, NameConstraints(dns, critical));
        if (problem == null) Assert.Null(LocalCa.Problem(ca));
        else Assert.Contains(problem, LocalCa.Problem(ca));
    }

    [Fact]
    public void Other_names_are_not_the_bpp_ca()
    {
        using var ca = Make("Some Other CA", isCa: true, NameConstraints(new[] { "localhost" }, critical: true));
        Assert.False(LocalCa.IsNamedLikeBppCa(ca));
        Assert.NotNull(LocalCa.Problem(ca));
    }

    [Fact]
    public void Excluded_only_or_unreadable_constraints_are_rejected()
    {
        var excludedOnly = new AsnWriter(AsnEncodingRules.DER);
        using (excludedOnly.PushSequence())
        using (excludedOnly.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true)))
        using (excludedOnly.PushSequence())
            excludedOnly.WriteCharacterString(UniversalTagNumber.IA5String, "example.com", new Asn1Tag(TagClass.ContextSpecific, 2));
        using var a = Make(LocalCa.CommonName, isCa: true, new X509Extension("2.5.29.30", excludedOnly.Encode(), critical: true));
        Assert.NotNull(LocalCa.Problem(a));

        using var b = Make(LocalCa.CommonName, isCa: true, new X509Extension("2.5.29.30", new byte[] { 0x04, 0x01, 0x00 }, critical: true));
        Assert.NotNull(LocalCa.Problem(b));
    }

    private static X509Extension NameConstraints(string[] dns, bool critical)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
        {
            foreach (var name in dns)
                using (w.PushSequence())
                    w.WriteCharacterString(UniversalTagNumber.IA5String, name, new Asn1Tag(TagClass.ContextSpecific, 2));
            using (w.PushSequence())
                w.WriteOctetString(new byte[8], new Asn1Tag(TagClass.ContextSpecific, 7)); // 0.0.0.0/0
        }
        return new X509Extension("2.5.29.30", w.Encode(), critical);
    }

    private static X509Certificate2 Make(string commonName, bool isCa, X509Extension nameConstraints)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + commonName + ", O=BPP", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(isCa, false, 0, critical: true));
        request.CertificateExtensions.Add(nameConstraints);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }
}
