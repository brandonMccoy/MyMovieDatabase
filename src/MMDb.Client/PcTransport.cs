using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MMDb.Client;

/// <summary>
/// Which PC a connection may talk to, by the SHA-256 fingerprint of its certificate. The PC's certificate is
/// its own (nobody vouches for it), so the fingerprint from the pairing QR code is the only thing checked.
/// </summary>
/// <param name="expected">Hex; <see langword="null"/> accepts whoever answers (pairing by typed address) and notes who it was.</param>
public sealed class CertificatePin(string? expected)
{
    public string? Expected { get; } = expected;

    /// <summary>The fingerprint of the certificate last presented.</summary>
    public string? Seen { get; private set; }

    /// <summary>A certificate other than the expected one was presented.</summary>
    public bool Rejected { get; private set; }

    public bool Accepts(X509Certificate? certificate)
    {
        if (certificate is null)
        {
            return false;
        }

        Seen = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
        var accepted = Expected is null || string.Equals(Expected, Seen, StringComparison.OrdinalIgnoreCase);
        Rejected = !accepted;
        return accepted;
    }
}

/// <summary>How the phone opens connections to the PC. Tests swap in an in-memory server.</summary>
public interface IPcTransport
{
    HttpMessageHandler CreateHandler(CertificatePin pin);
}

/// <summary>HTTPS over the home network to the pinned PC.</summary>
public sealed class TlsTransport : IPcTransport
{
    /// <summary>A PC that is off or on another network doesn't answer; don't keep the user waiting.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);

    public HttpMessageHandler CreateHandler(CertificatePin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = ConnectTimeout,
            UseProxy = false,
            UseCookies = false,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        };

        // Not the usual checks (name, issuer): only that it is the very certificate the phone paired with.
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) => pin.Accepts(certificate);
        return handler;
    }
}
