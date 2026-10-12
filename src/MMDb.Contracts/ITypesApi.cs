using MMDb.Contracts.Types;

namespace MMDb.Contracts;

/// <summary>
/// Which types of collection (movies, comics) Shelf shows on this device. Chosen in the PC's Settings; a paired
/// phone follows its PC. A type not in use is hidden, never deleted.
/// </summary>
public interface ITypesApi
{
    event Action? Changed;

    /// <summary>Every type until <see cref="LoadAsync"/> has read what was chosen.</summary>
    TypesInUseDto InUse { get; }

    /// <summary>Reads what was chosen last time. Called once at startup, before the first screen.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Chooses the types to show. At least one stays in use: a choice of none changes nothing.</summary>
    /// <returns>The types in use now.</returns>
    Task<TypesInUseDto> SetAsync(TypesInUseDto types, CancellationToken cancellationToken = default);
}
