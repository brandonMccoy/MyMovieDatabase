using Microsoft.Extensions.DependencyInjection;

namespace MMDb.UI;

public static class UiServices
{
    /// <summary>Registers the UI's own services. The host must also register an <see cref="Contracts.ICollectionApi"/>.</summary>
    public static IServiceCollection AddShelfUi(this IServiceCollection services) =>
        services.AddScoped<ShelfCounts>();
}
