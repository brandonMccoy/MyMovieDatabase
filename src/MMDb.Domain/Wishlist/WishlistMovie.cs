using MMDb.Domain.Movies;

namespace MMDb.Domain.Wishlist;

/// <summary>A film to buy, on the one format it is wanted on. A film is on the wishlist once.</summary>
public sealed class WishlistMovie : ProductBase
{
    // For EF Core.
    private WishlistMovie()
    {
        Title = "";
    }

    public string Title { get; private set; }

    public int? Year { get; private set; }

    public IReadOnlyList<string> Directors { get; private set; } = [];

    public int? TmdbId { get; private set; }

    public string? TrailerYouTubeKey { get; private set; }

    public string? PosterPath { get; private set; }

    public MediaFormat Format { get; private set; }

    /// <summary>Validation errors for this input; empty when <see cref="Create"/> or <see cref="Want"/> would accept it.</summary>
    public static IReadOnlyList<ValidationError> Validate(WantedMovie wanted)
    {
        var errors = wanted.AsDetails().Validate().ToList();
        if (!Enum.IsDefined(wanted.Format))
        {
            errors.Add(new(WishlistFields.Format, "Choose the format you want."));
        }

        return errors;
    }

    /// <exception cref="DomainValidationException">The input is invalid (see <see cref="Validate"/>).</exception>
    public static WishlistMovie Create(WantedMovie wanted, DateTimeOffset now)
    {
        var item = new WishlistMovie { Id = Guid.CreateVersion7(now), CreatedAt = now };
        item.Want(wanted, now);
        return item;
    }

    /// <summary>Wanting a film again: what is known about it and the wanted format are replaced.</summary>
    /// <exception cref="DomainValidationException">The input is invalid (see <see cref="Validate"/>).</exception>
    public void Want(WantedMovie wanted, DateTimeOffset now)
    {
        var errors = Validate(wanted);
        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        var details = wanted.AsDetails().Normalized();
        Title = details.Title;
        Year = details.Year;
        Directors = details.Directors;
        TmdbId = details.TmdbId;
        TrailerYouTubeKey = details.TrailerYouTubeKey;
        PosterPath = details.PosterPath;
        Format = wanted.Format;
        Touch(now);
    }

    /// <summary>Wants the film on another format; everything else stays.</summary>
    /// <exception cref="DomainValidationException">There is no such format.</exception>
    public void WantOn(MediaFormat format, DateTimeOffset now)
    {
        if (!Enum.IsDefined(format))
        {
            throw new DomainValidationException([new(WishlistFields.Format, "Choose the format you want.")]);
        }

        Format = format;
        Touch(now);
    }

    /// <summary>
    /// Whether this is the film wanted: the same TMDB entry, or for a wish entered without one, the same title and year.
    /// </summary>
    public bool IsFor(int? tmdbId, string title, int? year) => TmdbId is { } id
        ? id == tmdbId
        : Year == year && string.Equals(Title, title.Trim(), StringComparison.OrdinalIgnoreCase);

    public bool IsFor(MovieDetails film) => IsFor(film.TmdbId, film.Title, film.Year);
}

/// <summary>User input for a film on the wishlist: enough to list it and to find it again when it is bought.</summary>
public sealed record WantedMovie
{
    public required string Title { get; init; }

    public int? Year { get; init; }

    public IReadOnlyList<string> Directors { get; init; } = [];

    public int? TmdbId { get; init; }

    public string? TrailerYouTubeKey { get; init; }

    /// <summary>TMDB's name for the poster image.</summary>
    public string? PosterPath { get; init; }

    public required MediaFormat Format { get; init; }

    internal MovieDetails AsDetails() => new()
    {
        Title = Title,
        Year = Year,
        Directors = Directors,
        TmdbId = TmdbId,
        TrailerYouTubeKey = TrailerYouTubeKey,
        PosterPath = PosterPath,
    };
}

/// <summary>Field keys used in <see cref="ValidationError.Field"/> for wishlist input, besides the film's or comic's own.</summary>
public static class WishlistFields
{
    public const string Format = nameof(Format);
}
