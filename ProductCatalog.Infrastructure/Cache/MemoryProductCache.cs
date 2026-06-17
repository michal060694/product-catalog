using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductCatalog.Domain;

namespace ProductCatalog.Infrastructure;

public class MemoryProductCache : IProductCache
{
    private readonly IMemoryCache _cache;
    private readonly IOptions<CacheSettings> _settings;
    private readonly ILogger<MemoryProductCache> _logger;

    
    // For very large catalogs (100k+ keys), replace with 64 striped locks to cap memory usage.
    private readonly ConcurrentDictionary<string, object> _keyLocks = new();
    private readonly ConcurrentDictionary<string, long> _generations = new();

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

    public Task<long> GetGenerationAsync(string key, CancellationToken ct = default)
    {
        return Task.FromResult(_generations.GetValueOrDefault(key, 0L));
    }

    public Task SetAsync(string key, Product product, long expectedGeneration, CancellationToken ct = default)
    {
        var lockObj = _keyLocks.GetOrAdd(key, _ => new object());

        lock (lockObj)
        {
            if (_generations.GetValueOrDefault(key, 0L) != expectedGeneration)
                return Task.CompletedTask;

            var existing = _cache.Get<Product>(key);
            if (existing is not null && existing.Version >= product.Version)
                return Task.CompletedTask;

            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_settings.Value.ProductTtlMinutes)
            };
            _cache.Set(key, product, options);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        var lockObj = _keyLocks.GetOrAdd(key, _ => new object());

        lock (lockObj)
        {
            _cache.Remove(key);
            _generations.AddOrUpdate(key, 1L, (_, g) => g + 1);
        }

        return Task.CompletedTask;
    }
}
