using AutoMapper;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ProductCatalog.Application;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IProductService, ProductService>();
        services.AddAutoMapper(cfg => cfg.AddProfile<ProductProfile>());
        services.AddValidatorsFromAssemblyContaining<CreateProductDtoValidator>();
        return services;
    }
}
