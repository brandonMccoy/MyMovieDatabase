using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MMDb.UI;

public static class UiServices
{
    /// <summary>
    /// Registers the UI's own services. The host must also register an <see cref="Contracts.ICollectionApi"/>
    /// and, on the PC, an <see cref="Contracts.IVaultApi"/>, an <see cref="Contracts.IStatsApi"/>, an <see cref="Contracts.IPhoneApi"/> and an
    /// <see cref="IFolderPicker"/>. The phone registers <see cref="ShelfDevice.Phone"/> first, an
    /// <see cref="Contracts.IPcLink"/> and an <see cref="Scanning.IBarcodeScanner"/>.
    /// </summary>
    public static IServiceCollection AddShelfUi(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(ShelfDevice.Pc);
        return services.AddScoped<ShelfCounts>().AddScoped<Scanning.ScanInbox>();
    }
}
