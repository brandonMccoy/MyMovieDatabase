using MMDb.Application.Types;
using MMDb.Contracts;
using MMDb.Contracts.Types;

namespace MMDb.Local;

/// <summary><see cref="ITypesApi"/> over the settings kept in the database on this device.</summary>
public sealed class InProcessTypesApi(TypeChoice choice) : ITypesApi
{
    public event Action? Changed;

    public TypesInUseDto InUse { get; private set; } = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default) => Use(await choice.ReadAsync(cancellationToken));

    public async Task<TypesInUseDto> SetAsync(TypesInUseDto types, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(types);
        Use(await choice.SaveAsync(new(types.Movies, types.Comics), cancellationToken));
        return InUse;
    }

    private void Use(TypesInUse types)
    {
        var inUse = new TypesInUseDto(types.Movies, types.Comics);
        if (inUse != InUse)
        {
            InUse = inUse;
            Changed?.Invoke();
        }
    }
}
