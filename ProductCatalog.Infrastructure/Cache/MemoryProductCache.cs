using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductCatalog.Domain.Entities;
using ProductCatalog.Domain.Repositories;

namespace ProductCatalog.Infrastructure.Cache;

public class MemoryProductCache : IProductCache
{
    private readonly IMemoryCache _cache;
    private readonly IOptions<CacheSettings> _settings;
    private readonly ILogger<MemoryProductCache> _logger;

    public MemoryProductCache(IMemoryCache cache, IOptions<CacheSettings> settings, ILogger<MemoryProductCache> logger)
    {
        _cache = cache;
        _settings = settings;
        _logger = logger;
    }

    public Task<Product?> GetAsync(string key, CancellationToken ct = default)
    {
        var product = _cache.Get<Product>(key);
        return Task.FromResult(product);
    }

    public Task SetAsync(string key, Product product, CancellationToken ct = default)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_settings.Value.ProductTtlMinutes)
        };
        _cache.Set(key, product, options);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }
}
