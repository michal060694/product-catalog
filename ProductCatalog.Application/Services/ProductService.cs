    using AutoMapper;
using Microsoft.Extensions.Logging;
using ProductCatalog.Domain;

namespace ProductCatalog.Application;

public class ProductService : IProductService
{
    private readonly IProductRepository _repo;
    private readonly IProductCache _cache;
    private readonly ISharedTaskStore _taskStore;
    private readonly IMapper _mapper;
    private readonly ILogger<ProductService> _logger;

    public ProductService(IProductRepository repo, IProductCache cache,
        ISharedTaskStore taskStore, IMapper mapper, ILogger<ProductService> logger)
    {
        _repo = repo;
        _cache = cache;
        _taskStore = taskStore;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<ProductDto> GetProductAsync(int id, CancellationToken ct = default)
    {
        var key = CacheKeys.ForProduct(id);

        var cached = await _cache.GetAsync(key, ct);
        if (cached is not null)
        {
            _logger.LogInformation("Cache HIT for Product ID: {ProductId}.", id);
            return _mapper.Map<ProductDto>(cached);
        }

        _logger.LogInformation("Cache MISS for Product ID: {ProductId}. Fetching from repository.", id);

        var product = await _taskStore.GetOrAddAsync(key, async () =>
        {
            var gen = await _cache.GetGenerationAsync(key, CancellationToken.None);
            var p = _repo.GetById(id);
            if (p is not null)
                await _cache.SetAsync(key, p, gen, CancellationToken.None);
            return p;
        });

        if (product is null)
            throw new ProductNotFoundException(id);

        return _mapper.Map<ProductDto>(product);
    }

    public Task<ProductDto> CreateProductAsync(CreateProductDto dto, CancellationToken ct = default)
    {
        var product = _mapper.Map<Product>(dto);

        _repo.Add(product);

        _logger.LogInformation("Product created with ID: {ProductId}.", product.Id);

        return Task.FromResult(_mapper.Map<ProductDto>(product));
    }

    public async Task<ProductDto> UpdateProductAsync(int id, UpdateProductDto dto, CancellationToken ct = default)
    {
        var existing = _repo.GetById(id);
        if (existing is null)
            throw new ProductNotFoundException(id);

        _mapper.Map(dto, existing);
        _repo.Update(existing);

        var key = CacheKeys.ForProduct(id);
        await _cache.RemoveAsync(key, ct);
        _logger.LogInformation("Cache INVALIDATED for Product ID: {ProductId} after update.", id);

        return _mapper.Map<ProductDto>(existing);
    }
}
