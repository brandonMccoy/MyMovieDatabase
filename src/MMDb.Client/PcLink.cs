using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MMDb.Contracts;
using MMDb.Contracts.Phone;

namespace MMDb.Client;

/// <summary>What the phone keeps about its PC.</summary>
/// <param name="Hosts">The PC's addresses; the one that answered last comes first.</param>
/// <param name="Fingerprint">SHA-256 of the PC's certificate, in hex.</param>
/// <param name="Token">Proves it is this phone. Kept in the phone's secure storage.</param>
public sealed record PcLinkRecord(IReadOnlyList<string> Hosts, int Port, string Fingerprint, string Token, string PcName);

/// <summary>Port: where the phone keeps <see cref="PcLinkRecord"/> between runs.</summary>
public interface ILinkStore
{
    Task<PcLinkRecord?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(PcLinkRecord record, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <param name="DeviceName">What the PC lists this phone as, e.g. "Pixel 8".</param>
public sealed record PhoneIdentity(string DeviceName);

/// <summary><see cref="IPcLink"/> over HTTPS: pairs, remembers the PC, and sends every request with the phone's token.</summary>
public sealed class PcLink(ILinkStore store, IPcTransport transport, PhoneIdentity identity) : IPcLink, IDisposable
{
    public const int DefaultPort = 5243;

    /// <summary>Long enough for the PC to ask the online databases (it gives up after 20 seconds itself).</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(40);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private PcLinkRecord? _record;
    private HttpClient? _http;

    public event Action? Changed;

    public PcLinkState State { get; private set; } = PcLinkState.NotPaired;

    public string? PcName => _record?.PcName;

    public string? Address => _record is { Hosts: [var host, ..] } record ? Authority(host, record.Port) : null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Use(await store.LoadAsync(cancellationToken));
        Set(_record is null ? PcLinkState.NotPaired : PcLinkState.Unknown);
    }

    public Task<PairStatus> PairAsync(string qrText, CancellationToken cancellationToken = default) =>
        PairingLink.TryParse(qrText, out var link)
            ? PairAsync(link.Hosts, link.Port, link.Code, link.Fingerprint, cancellationToken)
            : Task.FromResult(PairStatus.NotACode);

    public Task<PairStatus> PairAsync(string address, string code, CancellationToken cancellationToken = default)
    {
        var typed = (address ?? "").Trim();
        if (typed.Contains("://", StringComparison.Ordinal))
        {
            typed = typed[(typed.IndexOf("://", StringComparison.Ordinal) + 3)..];
        }

        typed = typed.TrimEnd('/');
        var port = DefaultPort;
        if (typed.LastIndexOf(':') is var colon and > 0)
        {
            if (!int.TryParse(typed[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
            {
                return Task.FromResult(PairStatus.NotACode);
            }

            typed = typed[..colon];
        }

        var digits = (code ?? "").Replace(" ", "", StringComparison.Ordinal);
        return Uri.CheckHostName(typed) == UriHostNameType.Unknown || digits.Length == 0
            ? Task.FromResult(PairStatus.NotACode)
            : PairAsync([typed], port, digits, fingerprint: null, cancellationToken);
    }

    public async Task<PcLinkState> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_record is null)
        {
            return State;
        }

        try
        {
            using var response = await SendAsync(HttpMethod.Get, "session", content: null, cancellationToken);
            Set(PcLinkState.Connected);
        }
        catch (PcUnavailableException)
        {
            // SendAsync has set the state.
        }

        return State;
    }

    public async Task ForgetAsync(CancellationToken cancellationToken = default)
    {
        await store.ClearAsync(cancellationToken);
        Use(null);
        Set(PcLinkState.NotPaired);
    }

    /// <summary>Sends a request to the PC with this phone's token, trying each of the PC's addresses.</summary>
    /// <param name="path">Relative to <c>/api/v1/</c>.</param>
    /// <exception cref="PcUnavailableException">Not paired, the PC didn't answer, or it no longer knows this phone.</exception>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken = default)
    {
        if (_record is not { } record || _http is not { } http)
        {
            throw new PcUnavailableException("This phone isn't paired with a PC.");
        }

        // Buffered, so the request can be sent again to the PC's next address.
        var body = content is null ? null : await content.ReadAsByteArrayAsync(cancellationToken);
        Exception? failure = null;
        foreach (var host in record.Hosts)
        {
            using var request = new HttpRequestMessage(method, new Uri($"https://{Authority(host, record.Port)}/api/v1/{path}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", record.Token);
            if (body is not null)
            {
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = content!.Headers.ContentType;
            }

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellationToken);
            }
            catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                failure = e;
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                Set(PcLinkState.Unpaired);
                throw new PcUnavailableException("The PC no longer knows this phone.");
            }

            await ReachedAtAsync(host, cancellationToken);
            return response;
        }

        Set(PcLinkState.Unreachable);
        throw new PcUnavailableException("The PC didn't answer.", failure);
    }

    public void Dispose()
    {
        _http?.Dispose();
        _gate.Dispose();
    }

    private async Task<PairStatus> PairAsync(IReadOnlyList<string> hosts, int port, string code, string? fingerprint, CancellationToken cancellationToken)
    {
        var pin = new CertificatePin(fingerprint);
        using var http = new HttpClient(transport.CreateHandler(pin)) { Timeout = RequestTimeout };
        var wrongPc = false;
        foreach (var host in hosts)
        {
            HttpResponseMessage response;
            try
            {
                response = await http.PostAsJsonAsync(
                    new Uri($"https://{Authority(host, port)}/api/v1/pair"), new PairRequest(code, identity.DeviceName), ShelfJson.Options, cancellationToken);
            }
            catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                wrongPc |= pin.Rejected;
                continue;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode || await response.Content.ReadFromJsonAsync<PairResponse>(ShelfJson.Options, cancellationToken) is not { } paired)
                {
                    return PairStatus.WrongCode;
                }

                // Typed address: the certificate this PC showed is the one to expect from now on.
                var record = new PcLinkRecord(
                    [host, .. hosts.Where(h => h != host)], port, fingerprint ?? pin.Seen ?? "", paired.Token, paired.PcName);
                await store.SaveAsync(record, cancellationToken);
                Use(record);
                Set(PcLinkState.Connected);
                return PairStatus.Paired;
            }
        }

        return wrongPc ? PairStatus.WrongPc : PairStatus.Unreachable;
    }

    // The address that answered goes first, so the next request doesn't wait on a dead one.
    private async Task ReachedAtAsync(string host, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_record is { } record && record.Hosts[0] != host && record.Hosts.Contains(host))
            {
                _record = record with { Hosts = [host, .. record.Hosts.Where(h => h != host)] };
                await store.SaveAsync(_record, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }

        Set(PcLinkState.Connected);
    }

    private void Use(PcLinkRecord? record)
    {
        _http?.Dispose();
        _record = record;
        _http = record is null ? null : new HttpClient(transport.CreateHandler(new CertificatePin(record.Fingerprint))) { Timeout = RequestTimeout };
    }

    private void Set(PcLinkState state)
    {
        if (State != state)
        {
            State = state;
            Changed?.Invoke();
        }
    }

    private static string Authority(string host, int port) =>
        (host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host) + ":" + port.ToString(CultureInfo.InvariantCulture);
}
