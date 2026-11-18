using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LANVC.Security.Certificates;

public static class CertificateManager
{
    public static X509Certificate2 GenerateServerCertificate(string commonName = "LANVC-Server", int validYears = 5)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest($"CN={commonName}", ecdsa, HashAlgorithmName.SHA256);

        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        req.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Authentication
                critical: false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(commonName);
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        req.CertificateExtensions.Add(sanBuilder.Build());

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore.AddYears(validYears);

        var cert = req.CreateSelfSigned(notBefore, notAfter);

        // Windows SslStream / MsQuic requires an exportable PFX
        byte[] pfx = cert.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }

    public static string GetSha256Fingerprint(X509Certificate2 certificate)
    {
        byte[] hash = SHA256.HashData(certificate.RawData);
        var sb = new StringBuilder("SHA256:");
        for (int i = 0; i < hash.Length; i++)
        {
            if (i > 0) sb.Append(':');
            sb.Append(hash[i].ToString("X2"));
        }
        return sb.ToString();
    }

    public static bool ValidateFingerprint(X509Certificate2 certificate, string? expectedFingerprint)
    {
        if (string.IsNullOrWhiteSpace(expectedFingerprint))
            return true; // No pinning enforced

        string actual = GetSha256Fingerprint(certificate);
        return string.Equals(actual, expectedFingerprint.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
