using AutoMapper;
using Microsoft.Extensions.Logging;
using ProductCatalog.Application.DTOs;
using ProductCatalog.Domain.Cache;
using ProductCatalog.Domain.Entities;
using ProductCatalog.Domain.Repositories;

namespace ProductCatalog.Application.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _repo;
    private readonly IProductCache _cache;
    private readonly IMapper _mapper;
    private readonly ILogger<ProductService> _logger;

    public ProductService(IProductRepository repo,IProductCache cache,IMapper mapper,ILogger<ProductService> logger)
    {
        _repo = repo;
        _cache = cache;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<ProductDto?> GetProductAsync(int id, CancellationToken ct = default)
    {
        var key = CacheKeys.ForProduct(id);

        var cached = await _cache.GetAsync(key, ct);
        if (cached is not null)
        {
            _logger.LogInformation("Cache HIT for key {Key}", key);
            return _mapper.Map<ProductDto>(cached);
        }

        _logger.LogInformation("Cache MISS for key {Key}", key);

        var product = _repo.GetById(id);
        if (product is null)
            return null;

        await _cache.SetAsync(key, product, ct);
        return _mapper.Map<ProductDto>(product);
    }

    public async Task<ProductDto> CreateProductAsync(CreateProductDto dto, CancellationToken ct = default)
    {
        var product = _mapper.Map<Product>(dto);

        _repo.Add(product);

        var key = CacheKeys.ForProduct(product.Id);
        await _cache.RemoveAsync(key, ct);

        _logger.LogInformation("Created product with Id {Id}", product.Id);

        return _mapper.Map<ProductDto>(product);
    }
}
