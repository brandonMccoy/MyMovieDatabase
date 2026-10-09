namespace MMDb.Domain.Movies;

/// <summary>User input for one owned format of a film.</summary>
public sealed record FormatDetails(MediaFormat Format, string? Upc = null, string? FilePath = null)
{
    internal Upc? ParsedUpc => Domain.Upc.TryParse(Upc, out var upc) ? upc : null;

    internal string? NormalizedFilePath => string.IsNullOrWhiteSpace(FilePath) ? null : FilePath.Trim();

    public IReadOnlyList<ValidationError> Validate()
    {
        var errors = new List<ValidationError>();

        if (!Enum.IsDefined(Format))
        {
            errors.Add(new(MovieFields.Formats, $"Unknown format {Format}."));
            return errors;
        }

        if (!string.IsNullOrWhiteSpace(Upc) && !Domain.Upc.TryParse(Upc, out _))
        {
            errors.Add(new(MovieFields.Upc(Format), "Not a valid UPC or EAN barcode (12 or 13 digits with a matching check digit)."));
        }

        if (!string.IsNullOrWhiteSpace(FilePath) && Format != MediaFormat.Digital)
        {
            errors.Add(new(MovieFields.FilePath(Format), "Only digital copies have a file path."));
        }

        return errors;
    }

    /// <summary>Validates a full set of owned formats: at least one, no repeats, each valid.</summary>
    public static IReadOnlyList<ValidationError> ValidateSet(IReadOnlyCollection<FormatDetails> formats)
    {
        var errors = new List<ValidationError>();

        if (formats.Count == 0)
        {
            errors.Add(new(MovieFields.Formats, "Add at least one format you own."));
        }

        foreach (var repeated in formats.GroupBy(f => f.Format).Where(g => g.Count() > 1))
        {
            errors.Add(new(MovieFields.Formats, $"{repeated.Key.DisplayName()} is listed more than once."));
        }

        errors.AddRange(formats.SelectMany(f => f.Validate()));
        return errors;
    }
}
