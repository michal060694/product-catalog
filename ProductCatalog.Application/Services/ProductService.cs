using AutoMapper;
using ProductCatalog.Application.DTOs;
using ProductCatalog.Domain.Repositories;

namespace ProductCatalog.Application.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _repo;
    private readonly IMapper _mapper;

    public ProductService(IProductRepository repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    public ProductDto? GetProduct(int id)
    {
        var product = _repo.GetById(id);
        return product is null ? null : _mapper.Map<ProductDto>(product);
    }
}
