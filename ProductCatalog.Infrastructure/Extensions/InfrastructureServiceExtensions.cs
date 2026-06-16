using Microsoft.Extensions.DependencyInjection;
using ProductCatalog.Domain.Repositories;
using ProductCatalog.Infrastructure.Repositories;

namespace ProductCatalog.Infrastructure.Extensions;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IProductRepository, InMemoryProductRepository>();
        return services;
    }
}
