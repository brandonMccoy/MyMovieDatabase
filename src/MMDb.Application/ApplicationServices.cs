using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MMDb.Application.Comics;
using MMDb.Application.Metadata;
using MMDb.Application.Movies;
using MMDb.Application.Vault;

namespace MMDb.Application;

public static class ApplicationServices
{
    /// <summary>Registers the use cases. Ports (repositories, online databases, cover store, vault writer, settings) come from the infrastructure.</summary>
    public static IServiceCollection AddShelfApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<MovieCatalog>();
        services.AddScoped<MovieLookup>();
        services.AddScoped<ComicCatalog>();
        services.AddScoped<ComicLookup>();
        services.AddScoped<VaultContentSource>();
        services.AddSingleton<VaultMirror>();
        services.AddScoped<VaultSync>();
        return services;
    }
}
