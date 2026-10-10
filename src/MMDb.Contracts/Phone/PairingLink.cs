using System.Globalization;

namespace MMDb.Contracts.Phone;

/// <summary>
/// What the PC's pairing QR code says: where the PC is, the one-time code, and the fingerprint of the PC's
/// certificate, so the phone knows it is talking to that PC and no other.
/// <c>shelf://pair?h=192.168.1.24&amp;p=5243&amp;c=482913&amp;f=AB12…&amp;n=DESK</c>
/// </summary>
/// <param name="Hosts">The PC's addresses on the local network, most likely first.</param>
/// <param name="Fingerprint">SHA-256 of the PC's certificate, in hex.</param>
public sealed record PairingLink(IReadOnlyList<string> Hosts, int Port, string Code, string Fingerprint, string? PcName = null)
{
    private const string Prefix = "shelf://pair?";

    public override string ToString() =>
        Prefix + string.Join('&', new[]
        {
            "h=" + string.Join(',', Hosts.Select(Uri.EscapeDataString)),
            "p=" + Port.ToString(CultureInfo.InvariantCulture),
            "c=" + Uri.EscapeDataString(Code),
            "f=" + Uri.EscapeDataString(Fingerprint),
            PcName is null ? null : "n=" + Uri.EscapeDataString(PcName),
        }.OfType<string>());

    public static bool TryParse(string? text, out PairingLink link)
    {
        link = null!;
        var trimmed = (text ?? "").Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in trimmed[Prefix.Length..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                values[parts[0]] = Uri.UnescapeDataString(parts[1]);
            }
        }

        var hosts = values.GetValueOrDefault("h", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0
            || hosts.Any(h => Uri.CheckHostName(h) == UriHostNameType.Unknown)
            || !int.TryParse(values.GetValueOrDefault("p"), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535
            || values.GetValueOrDefault("c") is not { Length: > 0 } code
            || values.GetValueOrDefault("f") is not { Length: 64 } fingerprint
            || !fingerprint.All(char.IsAsciiHexDigit))
        {
            return false;
        }

        link = new PairingLink(hosts, port, code, fingerprint.ToUpperInvariant(), values.GetValueOrDefault("n"));
        return true;
    }
}
