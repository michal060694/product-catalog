using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductCatalog.Domain.Entities;
using ProductCatalog.Infrastructure.Cache;

namespace ProductCatalog.Tests.Cache;

public class MemoryProductCacheVersionGuardTests
{
    private readonly MemoryProductCache _sut;

    public MemoryProductCacheVersionGuardTests()
    {
        var memCache = new MemoryCache(new MemoryCacheOptions());
        var settings = Options.Create(new CacheSettings { ProductTtlMinutes = 5 });
        _sut = new MemoryProductCache(memCache, settings, NullLogger<MemoryProductCache>.Instance);
    }

    [Fact]
    public async Task Given_CacheHasNewerVersion_WhenSetAsync_Then_DoesNotOverwrite()
    {
        var key   = "product:1";
        var fresh = new Product { Id = 1, Name = "After PUT",  Price = 200m, Version = 2 };
        var stale = new Product { Id = 1, Name = "Before PUT", Price = 100m, Version = 1 };

        await _sut.SetAsync(key, fresh);
        await _sut.SetAsync(key, stale);

        var cached = await _sut.GetAsync(key);
        cached!.Version.Should().Be(2);
        cached.Name.Should().Be("After PUT");
    }

    [Fact]
    public async Task Given_CacheHasSameVersion_WhenSetAsync_Then_DoesNotOverwrite()
    {
        var key      = "product:1";
        var first    = new Product { Id = 1, Name = "First",  Price = 100m, Version = 2 };
        var samever  = new Product { Id = 1, Name = "Second", Price = 200m, Version = 2 };

        await _sut.SetAsync(key, first);
        await _sut.SetAsync(key, samever);

        var cached = await _sut.GetAsync(key);
        cached!.Name.Should().Be("First");
    }

    [Fact]
    public async Task Given_CacheHasOlderVersion_WhenSetAsync_Then_Overwrites()
    {
        var key   = "product:1";
        var old   = new Product { Id = 1, Name = "Old", Price = 100m, Version = 1 };
        var newer = new Product { Id = 1, Name = "New", Price = 200m, Version = 2 };

        await _sut.SetAsync(key, old);
        await _sut.SetAsync(key, newer);

        var cached = await _sut.GetAsync(key);
        cached!.Version.Should().Be(2);
        cached.Name.Should().Be("New");
    }

    [Fact]
    public async Task Given_EmptyCache_WhenSetAsync_Then_Stores()
    {
        var key     = "product:1";
        var product = new Product { Id = 1, Name = "Laptop", Price = 4999m, Version = 1 };

        await _sut.SetAsync(key, product);

        var cached = await _sut.GetAsync(key);
        cached.Should().NotBeNull();
        cached!.Version.Should().Be(1);
    }

    [Fact]
    public async Task Given_ShortTtl_WhenTtlExpires_Then_GetReturnsNull()
    {
        var memCache = new MemoryCache(new MemoryCacheOptions());
        var settings = Options.Create(new CacheSettings { ProductTtlMinutes = 1.0 / 600.0 }); // ~100ms
        var sut = new MemoryProductCache(memCache, settings, NullLogger<MemoryProductCache>.Instance);

        var key     = "product:ttl";
        var product = new Product { Id = 1, Name = "Expiry Test", Price = 99m, Version = 1 };

        await sut.SetAsync(key, product);
        await Task.Delay(200);
        var cached = await sut.GetAsync(key);

        cached.Should().BeNull();
    }
}
