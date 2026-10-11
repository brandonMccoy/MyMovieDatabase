using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MMDb.Contracts;
using MMDb.Contracts.Phone;
using MMDb.UI.Scanning;

namespace MMDb.UI.Tests;

/// <summary>
/// The screens as the phone runs them: the collection is the phone's own copy, kept in step with a PC that can
/// be out of reach, and the camera is a fake that "reads" whatever a test hands it.
/// </summary>
public abstract class PhoneTestContext : ShelfTestContext
{
    protected PhoneTestContext()
    {
        Services.Replace(ServiceDescriptor.Singleton(ShelfDevice.Phone));
        Sync = new(Link);
        Services.AddSingleton<IPcLink>(Link);
        Services.AddSingleton<IPhoneSync>(Sync);
        Services.AddSingleton<IBarcodeScanner>(Scanner);
    }

    /// <summary>No signal: the phone's copy still answers, the online databases don't.</summary>
    private protected bool Signal
    {
        set
        {
            Application.Metadata.MetadataProblem? problem = value ? null : Application.Metadata.MetadataProblem.Unavailable;
            (Online.Tmdb.FailWith, Online.Barcodes.FailWith, Online.Metron.FailWith, Online.Books.FailWith) = (problem, problem, problem, problem);
        }
    }

    protected FakePcLink Link { get; } = new();

    protected FakePhoneSync Sync { get; }

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

/// <summary>
/// The phone's copy as its screens are told of it: synced an hour ago by default. A sync reaches the PC if
/// <see cref="FakePcLink.PcUp"/>, and then the copy is fresh.
/// </summary>
public sealed class FakePhoneSync(FakePcLink link) : IPhoneSync
{
    public static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 12, 0, TimeSpan.Zero);

    private readonly HashSet<Guid> _unsent = [];

    public event Action? Changed;

    public PhoneSyncStatus Status { get; private set; } = new(PhoneSyncState.Idle, Now.AddHours(-1), 0, []);

    public int Syncs { get; private set; }

    /// <summary>What a sync that reaches the PC ends in.</summary>
    public PhoneSyncState Outcome { get; set; } = PhoneSyncState.Idle;

    /// <summary>Set to have a sync take until the test lets it finish.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public void Set(PhoneSyncStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }

    public void Unsent(Guid id) => _unsent.Add(id);

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        Syncs++;
        if (await link.CheckAsync(cancellationToken) == PcLinkState.Connected)
        {
            if (Hold is { } hold)
            {
                Set(Status with { State = PhoneSyncState.Syncing });
                await hold.Task;
            }

            Set(Outcome == PhoneSyncState.Idle ? Status with { State = PhoneSyncState.Idle, SyncedAt = Now, Waiting = 0 } : Status with { State = Outcome });
        }
    }

    public bool IsUnsent(Guid id) => _unsent.Contains(id);

    public Task DismissAsync(CancellationToken cancellationToken = default)
    {
        Set(Status with { TurnedDown = [] });
        return Task.CompletedTask;
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
