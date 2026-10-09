using MMDb.Contracts.Movies;
using MMDb.UI.Components;

namespace MMDb.UI.Movies;

/// <summary>What the add/edit screen is editing, in the shape the inputs bind to.</summary>
public sealed class MovieForm
{
    public string Title { get; set; } = "";

    public int? Year { get; set; }

    public int? RuntimeMinutes { get; set; }

    public string? Rating { get; set; }

    /// <summary>Comma-separated, as typed.</summary>
    public string Directors { get; set; } = "";

    public List<string> Genres { get; } = [];

    public string? Overview { get; set; }

    public List<CastRow> Cast { get; } = [];

    public Dictionary<MovieFormatKind, FormatRow> Formats { get; } = Components.Formats.All.ToDictionary(f => f, _ => new FormatRow());

    // Not editable yet (they come from TMDB in milestone 2), but must survive an edit.
    public int? TmdbId { get; set; }

    public string? TrailerYouTubeKey { get; set; }

    /// <summary>The minimum to save: a title and at least one owned format. The PC checks the rest.</summary>
    public bool CanSave => Title.Trim().Length > 0 && Formats.Values.Any(f => f.Owned);

    public IReadOnlyList<string> DirectorList =>
        [.. Directors.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    public static MovieForm From(MovieDto movie)
    {
        var form = new MovieForm
        {
            Title = movie.Title,
            Year = movie.Year,
            RuntimeMinutes = movie.RuntimeMinutes,
            Rating = movie.Rating,
            Directors = string.Join(", ", movie.Directors),
            Overview = movie.Overview,
            TmdbId = movie.TmdbId,
            TrailerYouTubeKey = movie.TrailerYouTubeKey,
        };
        form.Genres.AddRange(movie.Genres);
        form.Cast.AddRange(movie.Cast.Select(c => new CastRow { Name = c.Name, Character = c.Character }));
        foreach (var owned in movie.Formats)
        {
            form.Formats[owned.Format] = new FormatRow { Owned = true, Upc = owned.Upc, FilePath = owned.FilePath };
        }

        return form;
    }

    /// <summary>Adds a genre unless it is blank or already there (ignoring case).</summary>
    public void AddGenre(string? genre)
    {
        var trimmed = genre?.Trim();
        if (!string.IsNullOrEmpty(trimmed) && !Genres.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            Genres.Add(trimmed);
        }
    }

    public SaveMovieRequest ToRequest() => new()
    {
        Title = Title.Trim(),
        Year = Year,
        RuntimeMinutes = RuntimeMinutes,
        Rating = Rating,
        Directors = DirectorList,
        Genres = [.. Genres],
        Overview = Overview,
        Cast = [.. Cast.Select(c => new CastMemberDto(c.Name, c.Character))],
        TmdbId = TmdbId,
        TrailerYouTubeKey = TrailerYouTubeKey,
        Formats = [.. Formats
            .Where(f => f.Value.Owned)
            .Select(f => new FormatRequest(
                f.Key,
                Components.Formats.IsDisc(f.Key) ? f.Value.Upc : null,
                Components.Formats.IsDisc(f.Key) ? null : f.Value.FilePath))],
    };
}

public sealed class CastRow
{
    public string Name { get; set; } = "";

    public string? Character { get; set; }
}

public sealed class FormatRow
{
    public bool Owned { get; set; }

    /// <summary>Discs only.</summary>
    public string? Upc { get; set; }

    /// <summary>Digital only.</summary>
    public string? FilePath { get; set; }
}
