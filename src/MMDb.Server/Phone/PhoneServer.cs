using MMDb.Contracts.Phone;

namespace MMDb.Server.Phone;

/// <summary>Whether phones can reach this PC right now, and where. Set by <see cref="PhoneHost"/>, read by Settings.</summary>
public sealed class PhoneServer
{
    private readonly Lock _gate = new();
    private PhoneServerDto _status = new(PhoneServerState.Off, PhoneHost.DefaultPort, []);

    public event Action? Changed;

    public PhoneServerDto Status { get { lock (_gate) { return _status; } } }

    /// <summary>SHA-256 of the PC's certificate in hex, once the server is listening.</summary>
    public string? Fingerprint { get; private set; }

    /// <summary>What the PC calls itself on phones.</summary>
    public string PcName { get; init; } = Environment.MachineName;

    public void Listening(int port, IReadOnlyList<string> addresses, string fingerprint)
    {
        Fingerprint = fingerprint;
        Set(new(PhoneServerState.Listening, port, addresses));
    }

    public void Failed(int port, string problem) => Set(new(PhoneServerState.Failed, port, [], problem));

    public void Stopped() => Set(Status with { State = PhoneServerState.Off, Addresses = [], Problem = null });

    private void Set(PhoneServerDto status)
    {
        lock (_gate)
        {
            _status = status;
        }

        Changed?.Invoke();
    }
}
