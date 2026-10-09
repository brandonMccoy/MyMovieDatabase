using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MMDb.Application.Metadata;
using MMDb.Application.Movies;

namespace MMDb.Application;

public static class ApplicationServices
{
    /// <summary>Registers the use cases. Ports (repositories, online databases, cover store) come from the infrastructure.</summary>
    public static IServiceCollection AddShelfApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<MovieCatalog>();
        services.AddScoped<MovieLookup>();
        return services;
    }
}
