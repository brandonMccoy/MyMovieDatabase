using MMDb.Application.Vault;

namespace MMDb.Application.Types;

/// <summary>
/// Which types of collection Shelf shows. A type not in use is hidden everywhere; what was saved for it stays
/// in the database and shows again when the type is back in use.
/// </summary>
public sealed record TypesInUse(bool Movies = true, bool Comics = true)
{
    public static TypesInUse All { get; } = new();

    public bool Any => Movies || Comics;
}

/// <summary>Keeps <see cref="TypesInUse"/> between runs.</summary>
public sealed class TypeChoice(ISettingStore settings)
{
    private const string MoviesKey = "types.movies";
    private const string ComicsKey = "types.comics";
    private const string Off = "off";

    /// <returns>Every type, until one was turned off.</returns>
    public async Task<TypesInUse> ReadAsync(CancellationToken cancellationToken = default)
    {
        var types = new TypesInUse(
            await settings.GetAsync(MoviesKey, cancellationToken) != Off,
            await settings.GetAsync(ComicsKey, cancellationToken) != Off);
        return types.Any ? types : TypesInUse.All;
    }

    /// <summary>Keeps the choice. Shelf always shows at least one type, so a choice of none is turned down.</summary>
    /// <returns>The types in use now.</returns>
    public async Task<TypesInUse> SaveAsync(TypesInUse types, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(types);
        if (!types.Any)
        {
            return await ReadAsync(cancellationToken);
        }

        await settings.SetAsync(MoviesKey, types.Movies ? null : Off, cancellationToken);
        await settings.SetAsync(ComicsKey, types.Comics ? null : Off, cancellationToken);
        return types;
    }
}
