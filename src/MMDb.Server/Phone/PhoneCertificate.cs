using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MMDb.Server.Phone;

/// <summary>
/// The PC's own certificate for HTTPS on the home network. Nobody vouches for it; a phone trusts it because
/// its fingerprint was in the pairing QR code. It is made once and kept, so paired phones keep recognizing the PC.
/// </summary>
public static class PhoneCertificate
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(365 * 20);

    /// <param name="path">A .pfx file beside the database; created if it isn't there, unreadable or about to expire.</param>
    public static X509Certificate2 LoadOrCreate(string path, DateTimeOffset now)
    {
        if (File.Exists(path))
        {
            try
            {
                var kept = X509CertificateLoader.LoadPkcs12FromFile(path, password: null);
                if (kept.HasPrivateKey && kept.NotAfter.ToUniversalTime() > now.UtcDateTime.AddDays(30))
                {
                    return kept;
                }

                kept.Dispose();
            }
            catch (CryptographicException)
            {
                // Damaged file: make a new certificate. Phones then need pairing again.
            }
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Shelf", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using var created = request.CreateSelfSigned(now.AddDays(-1), now + Lifetime);

        // Through a .pfx, so Windows gets a key it can use for TLS.
        var pfx = created.Export(X509ContentType.Pfx);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, password: null);
    }

    /// <summary>SHA-256 of the certificate, in upper-case hex: what the pairing QR code carries.</summary>
    public static string Fingerprint(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return certificate.GetCertHashString(HashAlgorithmName.SHA256);
    }
}
