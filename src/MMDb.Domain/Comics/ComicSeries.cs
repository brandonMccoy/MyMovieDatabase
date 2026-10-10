namespace MMDb.Domain.Comics;

/// <summary>
/// What Metron knows about a series: every issue number it lists, which gives a run's "N of M" and its missing
/// issues. Kept on the PC and fetched again when it is a week old.
/// </summary>
public sealed class ComicSeries
{
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(7);

    // For EF Core.
    private ComicSeries()
    {
        Name = "";
    }

    public int MetronId { get; private set; }

    public string Name { get; private set; }

    public int? YearBegan { get; private set; }

    public string? Publisher { get; private set; }

    /// <summary>Normalized (see <see cref="IssueNumber.Normalize"/>), in running order, no repeats.</summary>
    public IReadOnlyList<string> IssueNumbers { get; private set; } = [];

    public DateTimeOffset FetchedAt { get; private set; }

    public static ComicSeries Create(int metronId, DateTimeOffset now, SeriesFacts facts)
    {
        var series = new ComicSeries { MetronId = metronId };
        series.Refresh(facts, now);
        return series;
    }

    public void Refresh(SeriesFacts facts, DateTimeOffset now)
    {
        Name = facts.Name.Trim();
        YearBegan = facts.YearBegan;
        Publisher = string.IsNullOrWhiteSpace(facts.Publisher) ? null : facts.Publisher.Trim();
        IssueNumbers = [.. facts.IssueNumbers
            .Select(IssueNumber.Normalize)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(IssueNumber.Comparer)];
        FetchedAt = now;
    }

    public bool IsStale(DateTimeOffset now) => now - FetchedAt >= RefreshAfter;
}

/// <summary>A series as an online database describes it.</summary>
public sealed record SeriesFacts(string Name, int? YearBegan, string? Publisher, IReadOnlyList<string> IssueNumbers);
