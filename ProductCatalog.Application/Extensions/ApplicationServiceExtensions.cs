using AutoMapper;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ProductCatalog.Application.Mappings;
using ProductCatalog.Application.Services;
using ProductCatalog.Application.Validators;

namespace ProductCatalog.Application.Extensions;

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
