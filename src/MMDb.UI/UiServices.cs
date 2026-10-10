using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MMDb.UI;

public static class UiServices
{
    /// <summary>
    /// Registers the UI's own services. The host must also register an <see cref="Contracts.ICollectionApi"/>
    /// and, on the PC, an <see cref="Contracts.IVaultApi"/> and an <see cref="IFolderPicker"/>.
    /// </summary>
    public static IServiceCollection AddShelfUi(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        return services.AddScoped<ShelfCounts>();
    }
}
