using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProductCatalog.Domain.Repositories;
using ProductCatalog.Infrastructure.Cache;
using ProductCatalog.Infrastructure.Repositories;

namespace ProductCatalog.Infrastructure.Extensions;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CacheSettings>(configuration.GetSection("Cache"));
        services.AddMemoryCache();
        services.AddSingleton<IProductRepository, InMemoryProductRepository>();
        services.AddSingleton<IProductCache, MemoryProductCache>();
        return services;
    }
}
