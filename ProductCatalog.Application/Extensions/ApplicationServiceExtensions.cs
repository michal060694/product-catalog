using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using ProductCatalog.Application.Mappings;
using ProductCatalog.Application.Services;

namespace ProductCatalog.Application.Extensions;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IProductService, ProductService>();
        services.AddAutoMapper(cfg => cfg.AddProfile<ProductProfile>());
        return services;
    }
}
