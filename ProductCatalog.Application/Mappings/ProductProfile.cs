using AutoMapper;
using ProductCatalog.Domain;

namespace ProductCatalog.Application;

public class ProductProfile : Profile
{
    public ProductProfile()
    {
        CreateMap<Product, ProductDto>();

        CreateMap<CreateProductDto, Product>()
            .ForMember(dest => dest.Id,        opt => opt.Ignore())
            .ForMember(dest => dest.CostPrice, opt => opt.Ignore())
            .ForMember(dest => dest.Version,   opt => opt.Ignore());

        CreateMap<UpdateProductDto, Product>()
            .ForMember(dest => dest.Id,        opt => opt.Ignore())
            .ForMember(dest => dest.CostPrice, opt => opt.Ignore())
            .ForMember(dest => dest.Version,   opt => opt.Ignore());
    }
}
