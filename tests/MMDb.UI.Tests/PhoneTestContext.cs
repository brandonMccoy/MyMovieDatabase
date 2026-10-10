using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Phone;
using MMDb.Contracts.Scans;
using MMDb.Server;
using MMDb.UI.Scanning;

namespace MMDb.UI.Tests;

/// <summary>
/// The screens as the phone runs them: the collection is the PC's, reached through a link that can go down,
/// and the camera is a fake that "reads" whatever a test hands it.
/// </summary>
public abstract class PhoneTestContext : ShelfTestContext
{
    protected PhoneTestContext()
    {
        Services.Replace(ServiceDescriptor.Singleton(ShelfDevice.Phone));
        Services.AddSingleton<IPcLink>(Link);
        Services.AddSingleton<IBarcodeScanner>(Scanner);
        Services.Replace(ServiceDescriptor.Singleton<ICollectionApi>(
            services => new LinkedApi(ActivatorUtilities.CreateInstance<InProcessCollectionApi>(services), Link)));
    }

    protected FakePcLink Link { get; } = new();

    protected FakeScanner Scanner { get; } = new();

    protected ScanInbox Queue => Services.GetRequiredService<ScanInbox>();

    // The text as read on screen: runs of whitespace from the markup collapse to one space.
    protected static string Text(AngleSharp.Dom.IElement element) => System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();
}

/// <summary>A paired, connected phone by default. <see cref="PcUp"/> false makes every request fail as when the PC is off.</summary>
public sealed class FakePcLink : IPcLink
{
    public event Action? Changed;

    public PcLinkState State { get; private set; } = PcLinkState.Connected;

    public string? PcName { get; set; } = "DESK";

    public string? Address { get; set; } = "192.168.1.24:5243";

    public bool PcUp { get; set; } = true;

    /// <summary>What pairing answers; <see cref="PairStatus.Paired"/> also connects.</summary>
    public PairStatus PairAnswer { get; set; } = PairStatus.Paired;

    /// <summary>Every pairing attempt: the QR text, or "address|code".</summary>
    public List<string> PairAttempts { get; } = [];

    public int Checks { get; private set; }

    public void Set(PcLinkState state)
    {
        if (State != state)
        {
            State = state;
            Changed?.Invoke();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<PairStatus> PairAsync(string qrText, CancellationToken cancellationToken = default) => Pair(qrText);

    public Task<PairStatus> PairAsync(string address, string code, CancellationToken cancellationToken = default) => Pair($"{address}|{code}");

    public Task<PcLinkState> CheckAsync(CancellationToken cancellationToken = default)
    {
        Checks++;
        if (State is not (PcLinkState.NotPaired or PcLinkState.Unpaired))
        {
            Set(PcUp ? PcLinkState.Connected : PcLinkState.Unreachable);
        }

        return Task.FromResult(State);
    }

    public Task ForgetAsync(CancellationToken cancellationToken = default)
    {
        PcName = null;
        Address = null;
        Set(PcLinkState.NotPaired);
        return Task.CompletedTask;
    }

    private Task<PairStatus> Pair(string attempt)
    {
        PairAttempts.Add(attempt);
        if (PairAnswer == PairStatus.Paired)
        {
            PcName = "DESK";
            Set(PcLinkState.Connected);
        }

        return Task.FromResult(PairAnswer);
    }
}

/// <summary>The camera: a test calls <see cref="Read"/> with what it "sees".</summary>
public sealed class FakeScanner : IBarcodeScanner
{
    private Action<string>? _onCode;

    public ScannerStart Answer { get; set; } = ScannerStart.Started;

    /// <summary>What it was last started for; <see langword="null"/> while stopped.</summary>
    public ScanTarget? Running { get; private set; }

    public int Stops { get; private set; }

    public bool ReadAddOns { get; set; }

    public Task<ScannerStart> StartAsync(ScanTarget target, Action<string> onCode)
    {
        if (Answer == ScannerStart.Started)
        {
            Running = target;
            _onCode = onCode;
        }

        return Task.FromResult(Answer);
    }

    public Task StopAsync()
    {
        Stops++;
        Running = null;
        _onCode = null;
        return Task.CompletedTask;
    }

    public void Read(string code) => (_onCode ?? throw new InvalidOperationException("The scanner isn't running.")).Invoke(code);
}

/// <summary>The PC's collection as the phone reaches it: gone when the link is down.</summary>
internal sealed class LinkedApi(ICollectionApi pc, FakePcLink link) : ICollectionApi
{
    public event Action? Changed
    {
        add => pc.Changed += value;
        remove => pc.Changed -= value;
    }

    public Task<MovieListResponse> ListMoviesAsync(MovieListRequest request, CancellationToken cancellationToken = default) => Up().ListMoviesAsync(request, cancellationToken);

    public Task<MovieDto?> GetMovieAsync(Guid id, CancellationToken cancellationToken = default) => Up().GetMovieAsync(id, cancellationToken);

    public Task<SaveMovieResponse> AddMovieAsync(SaveMovieRequest request, CancellationToken cancellationToken = default) => Up().AddMovieAsync(request, cancellationToken);

    public Task<SaveMovieResponse> UpdateMovieAsync(Guid id, int version, SaveMovieRequest request, CancellationToken cancellationToken = default) => Up().UpdateMovieAsync(id, version, request, cancellationToken);

    public Task<bool> DeleteMovieAsync(Guid id, CancellationToken cancellationToken = default) => Up().DeleteMovieAsync(id, cancellationToken);

    public Task<MovieLookupResponse> LookupMovieAsync(string query, CancellationToken cancellationToken = default) => Up().LookupMovieAsync(query, cancellationToken);

    public Task<ComicListResponse> ListComicsAsync(ComicListRequest request, CancellationToken cancellationToken = default) => Up().ListComicsAsync(request, cancellationToken);

    public Task<ComicDto?> GetComicAsync(Guid id, CancellationToken cancellationToken = default) => Up().GetComicAsync(id, cancellationToken);

    public Task<SaveComicResponse> AddComicAsync(SaveComicRequest request, CancellationToken cancellationToken = default) => Up().AddComicAsync(request, cancellationToken);

    public Task<SaveComicResponse> UpdateComicAsync(Guid id, int version, SaveComicRequest request, CancellationToken cancellationToken = default) => Up().UpdateComicAsync(id, version, request, cancellationToken);

    public Task<bool> DeleteComicAsync(Guid id, CancellationToken cancellationToken = default) => Up().DeleteComicAsync(id, cancellationToken);

    public Task<ComicLookupResponse> LookupComicAsync(string query, CancellationToken cancellationToken = default) => Up().LookupComicAsync(query, cancellationToken);

    public Task<ScanResponse> LookupScanAsync(ScanRequest request, CancellationToken cancellationToken = default) => Up().LookupScanAsync(request, cancellationToken);

    private ICollectionApi Up()
    {
        if (!link.PcUp)
        {
            link.Set(PcLinkState.Unreachable);
            throw new PcUnavailableException("The PC didn't answer.");
        }

        return pc;
    }
}
