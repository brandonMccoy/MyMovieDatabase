using MMDb.Contracts.Phone;

namespace MMDb.Contracts;

/// <summary>The phone's link to its PC: pairing, and whether the PC can be reached. Phone only.</summary>
public interface IPcLink
{
    event Action? Changed;

    PcLinkState State { get; }

    /// <summary>The paired PC's name.</summary>
    string? PcName { get; }

    /// <summary>The address the PC was last reached at, e.g. <c>192.168.1.24:5243</c>.</summary>
    string? Address { get; }

    /// <summary>Loads the pairing kept on the phone. Called once at startup.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Pairs from the text of the QR code the PC shows.</summary>
    Task<PairStatus> PairAsync(string qrText, CancellationToken cancellationToken = default);

    /// <summary>Pairs from a typed address (<c>192.168.1.24</c> or <c>192.168.1.24:5243</c>) and the six-digit code.</summary>
    Task<PairStatus> PairAsync(string address, string code, CancellationToken cancellationToken = default);

    /// <summary>Asks the PC whether it is there and still knows this phone.</summary>
    Task<PcLinkState> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets the PC, so the phone can pair again.</summary>
    Task ForgetAsync(CancellationToken cancellationToken = default);
}

/// <summary>The PC did not answer, or no longer knows this phone; see <see cref="IPcLink.State"/>.</summary>
public sealed class PcUnavailableException : Exception
{
    public PcUnavailableException()
    {
    }

    public PcUnavailableException(string message)
        : base(message)
    {
    }

    public PcUnavailableException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
