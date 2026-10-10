namespace MMDb.Domain.Movies;

/// <summary>A film in the collection: one row per film, with every format it is owned on.</summary>
public sealed class Movie : ProductBase
{
    private readonly List<MovieFormat> _formats = [];

    // For EF Core.
    private Movie()
    {
        Title = "";
    }

    public string Title { get; private set; }

    public int? Year { get; private set; }

    public IReadOnlyList<string> Directors { get; private set; } = [];

    public IReadOnlyList<CastMember> Cast { get; private set; } = [];

    /// <summary>In credit order (see <see cref="CrewCredits.InCreditOrder"/>).</summary>
    public IReadOnlyList<CrewMember> Crew { get; private set; } = [];

    public IReadOnlyList<string> Genres { get; private set; } = [];

    public int? RuntimeMinutes { get; private set; }

    public string? Rating { get; private set; }

    public string? Overview { get; private set; }

    public int? TmdbId { get; private set; }

    public string? TrailerYouTubeKey { get; private set; }

    public string? PosterPath { get; private set; }

    /// <summary>Owned formats in display order (4K UHD, Blu-ray, DVD, Digital).</summary>
    public IReadOnlyList<MovieFormat> Formats => [.. _formats.OrderBy(f => f.Format)];

    /// <summary>When a format was last added: what "Recently added" sorts by.</summary>
    public DateTimeOffset LastAddedAt => _formats.Count == 0 ? CreatedAt : _formats.Max(f => f.AddedAt);

    public MovieDetails Details => new()
    {
        Title = Title,
        Year = Year,
        Directors = Directors,
        Cast = Cast,
        Crew = Crew,
        Genres = Genres,
        RuntimeMinutes = RuntimeMinutes,
        Rating = Rating,
        Overview = Overview,
        TmdbId = TmdbId,
        TrailerYouTubeKey = TrailerYouTubeKey,
        PosterPath = PosterPath,
    };

    public MovieListing Listing => new(
        Id, Title, Year, Directors, Genres, [.. _formats.Select(f => f.Format).Order()], CreatedAt, LastAddedAt, TmdbId, CoverImage);

    /// <summary>Validation errors for this input; empty when <see cref="Create"/> or <see cref="Update"/> would accept it.</summary>
    public static IReadOnlyList<ValidationError> Validate(MovieDetails details, IReadOnlyCollection<FormatDetails> formats) =>
        [.. details.Validate(), .. FormatDetails.ValidateSet(formats)];

    /// <exception cref="DomainValidationException">The input is invalid (see <see cref="Validate"/>).</exception>
    public static Movie Create(MovieDetails details, IReadOnlyCollection<FormatDetails> formats, DateTimeOffset now)
    {
        ThrowIfInvalid(details, formats);

        var movie = new Movie { Id = Guid.CreateVersion7(now), CreatedAt = now };
        movie.Apply(details.Normalized());
        movie._formats.AddRange(formats.Select(f => new MovieFormat(f, now)));
        movie.Touch(now);
        return movie;
    }

    /// <summary>
    /// Replaces the details and the set of owned formats. Formats kept from before keep their added date;
    /// formats left out are removed.
    /// </summary>
    /// <exception cref="DomainValidationException">The input is invalid (see <see cref="Validate"/>).</exception>
    public void Update(MovieDetails details, IReadOnlyCollection<FormatDetails> formats, DateTimeOffset now)
    {
        ThrowIfInvalid(details, formats);

        Apply(details.Normalized());

        _formats.RemoveAll(owned => formats.All(f => f.Format != owned.Format));
        foreach (var input in formats)
        {
            var owned = _formats.Find(f => f.Format == input.Format);
            if (owned is null)
            {
                _formats.Add(new MovieFormat(input, now));
            }
            else
            {
                owned.Apply(input);
            }
        }

        Touch(now);
    }

    /// <summary>Adds the formats not owned yet and leaves the details alone: buying a film again on another format.</summary>
    /// <returns>The formats added; empty (and nothing changed) if every one was owned already.</returns>
    /// <exception cref="DomainValidationException">The formats are invalid (see <see cref="FormatDetails.ValidateSet"/>).</exception>
    public IReadOnlyList<MediaFormat> AddFormats(IReadOnlyCollection<FormatDetails> formats, DateTimeOffset now)
    {
        var errors = FormatDetails.ValidateSet(formats);
        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        var added = formats.Where(f => !Owns(f.Format)).ToList();
        if (added.Count == 0)
        {
            return [];
        }

        _formats.AddRange(added.Select(f => new MovieFormat(f, now)));
        Touch(now);
        return [.. added.Select(f => f.Format).Order()];
    }

    /// <summary>
    /// Points the film's digital copy at a video file on the PC. A film not owned digitally yet now is; one that
    /// is has its file replaced.
    /// </summary>
    public void LinkFile(string path, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_formats.Find(f => f.Format == MediaFormat.Digital) is { } digital)
        {
            digital.MoveTo(path);
        }
        else
        {
            _formats.Add(new MovieFormat(new FormatDetails(MediaFormat.Digital, FilePath: path), now));
        }

        Touch(now);
    }

    /// <summary>Stops owning the film on one format.</summary>
    /// <returns>
    /// <see langword="false"/> (and nothing changed) if the film isn't owned on it, or on nothing else: a film
    /// in the collection is owned on at least one format.
    /// </returns>
    public bool RemoveFormat(MediaFormat format, DateTimeOffset now)
    {
        if (!Owns(format) || _formats.Count == 1)
        {
            return false;
        }

        _formats.RemoveAll(f => f.Format == format);
        Touch(now);
        return true;
    }

    public bool Owns(MediaFormat format) => _formats.Exists(f => f.Format == format);

    private void Apply(MovieDetails details)
    {
        Title = details.Title;
        Year = details.Year;
        Directors = details.Directors;
        Cast = details.Cast;
        Crew = details.Crew;
        Genres = details.Genres;
        RuntimeMinutes = details.RuntimeMinutes;
        Rating = details.Rating;
        Overview = details.Overview;
        TmdbId = details.TmdbId;
        TrailerYouTubeKey = details.TrailerYouTubeKey;
        PosterPath = details.PosterPath;
    }

    private static void ThrowIfInvalid(MovieDetails details, IReadOnlyCollection<FormatDetails> formats)
    {
        var errors = Validate(details, formats);
        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }
    }
}
