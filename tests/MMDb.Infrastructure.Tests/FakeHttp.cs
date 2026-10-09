using System.Net;
using System.Text;

namespace MMDb.Infrastructure.Tests;

/// <summary>Answers every request with a canned response and remembers what was asked.</summary>
internal sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly List<(Uri Uri, string? Authorization)> _requests = [];

    public IReadOnlyList<(Uri Uri, string? Authorization)> Requests { get { lock (_requests) { return [.. _requests]; } } }

    public static FakeHttp Replaying(string fixture, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => Respond(status, Fixtures.Read(fixture)));

    public static FakeHttp Failing(Exception exception) => new(_ => throw exception);

    public static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public HttpClient Client(string baseAddress) => new(this, disposeHandler: false) { BaseAddress = new Uri(baseAddress) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_requests)
        {
            _requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString()));
        }

        return Task.FromResult(respond(request));
    }
}

internal static class Fixtures
{
    /// <param name="name">Relative to <c>Fixtures/</c>, e.g. <c>tmdb/search-heat.json</c>.</param>
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
