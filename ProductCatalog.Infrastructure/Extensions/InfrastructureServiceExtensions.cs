using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProductCatalog.Domain;

namespace ProductCatalog.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CacheSettings>(configuration.GetSection("Cache"));
        services.AddMemoryCache();
        services.AddSingleton<IProductRepository, InMemoryProductRepository>();
        services.AddSingleton<IProductCache, MemoryProductCache>();
        services.AddSingleton<ISharedTaskStore, SharedTaskStore>();
        return services;
    }
}
