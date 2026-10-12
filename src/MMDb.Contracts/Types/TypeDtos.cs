using System.Text.Json.Serialization;
using MMDb.Contracts.Scans;

namespace MMDb.Contracts.Types;

/// <summary>The types of collection Shelf shows. There is always at least one.</summary>
public sealed record TypesInUseDto(bool Movies = true, bool Comics = true)
{
    /// <summary>The one type in use; <see langword="null"/> when there are several.</summary>
    [JsonIgnore]
    public ProductKind? Only => Movies && Comics ? null : Comics ? ProductKind.Comic : ProductKind.Movie;

    public bool Uses(ProductKind kind) => kind == ProductKind.Comic ? Comics : Movies;

    public TypesInUseDto With(ProductKind kind, bool use) => kind == ProductKind.Comic ? this with { Comics = use } : this with { Movies = use };
}
