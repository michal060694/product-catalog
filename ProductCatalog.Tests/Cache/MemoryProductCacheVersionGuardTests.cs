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

        await _sut.SetAsync(key, fresh, expectedGeneration: 0L);
        await _sut.SetAsync(key, stale, expectedGeneration: 0L);

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

        await _sut.SetAsync(key, first,   expectedGeneration: 0L);
        await _sut.SetAsync(key, samever, expectedGeneration: 0L);

        var cached = await _sut.GetAsync(key);
        cached!.Name.Should().Be("First");
    }

    [Fact]
    public async Task Given_CacheHasOlderVersion_WhenSetAsync_Then_Overwrites()
    {
        var key   = "product:1";
        var old   = new Product { Id = 1, Name = "Old", Price = 100m, Version = 1 };
        var newer = new Product { Id = 1, Name = "New", Price = 200m, Version = 2 };

        await _sut.SetAsync(key, old,   expectedGeneration: 0L);
        await _sut.SetAsync(key, newer, expectedGeneration: 0L);

        var cached = await _sut.GetAsync(key);
        cached!.Version.Should().Be(2);
        cached.Name.Should().Be("New");
    }

    [Fact]
    public async Task Given_EmptyCache_WhenSetAsync_Then_Stores()
    {
        var key     = "product:1";
        var product = new Product { Id = 1, Name = "Laptop", Price = 4999m, Version = 1 };

        await _sut.SetAsync(key, product, expectedGeneration: 0L);

        var cached = await _sut.GetAsync(key);
        cached.Should().NotBeNull();
        cached!.Version.Should().Be(1);
    }

    [Fact]
    public async Task Given_ConcurrentSetAsync_WhenVersionsDiffer_Then_HigherVersionSurvives()
    {
        var key = "product:concurrent";
        const int threadCount = 100;

        var tasks = Enumerable.Range(1, threadCount)
            .Select(v => _sut.SetAsync(key, new Product { Id = 1, Name = $"v{v}", Price = v, Version = v }, expectedGeneration: 0L))
            .ToArray();

        await Task.WhenAll(tasks);

        var cached = await _sut.GetAsync(key);
        cached.Should().NotBeNull();
        cached!.Version.Should().Be(threadCount);
    }

    [Fact]
    public async Task Given_RemoveAsync_WhenStaleSetAsyncUsesOldGeneration_Then_WriteIsRejected()
    {
        var key   = "product:gen";
        var stale = new Product { Id = 1, Name = "stale", Price = 100m, Version = 1 };

        var genBefore = await _sut.GetGenerationAsync(key);   // 0

        await _sut.RemoveAsync(key);                          // gen → 1

        await _sut.SetAsync(key, stale, genBefore);           // 0 ≠ 1 → rejected

        (await _sut.GetAsync(key)).Should().BeNull("stale write must be rejected by generation mismatch");
    }

    [Fact]
    public async Task Given_RemoveAsync_WhenFreshSetAsyncUsesCapturedGeneration_Then_Succeeds()
    {
        var key     = "product:gen";
        var product = new Product { Id = 1, Name = "current", Price = 200m, Version = 2 };

        await _sut.RemoveAsync(key);                         // gen → 1

        var genAfter = await _sut.GetGenerationAsync(key);   // 1
        await _sut.SetAsync(key, product, genAfter);         // 1 == 1 → accepted

        var cached = await _sut.GetAsync(key);
        cached.Should().NotBeNull();
        cached!.Version.Should().Be(2);
        cached.Name.Should().Be("current");
    }

    [Fact]
    public async Task Given_MultipleRemoves_WhenEachRemove_Then_GenerationIncrements()
    {
        var key = "product:gen2";

        await _sut.RemoveAsync(key);   // gen → 1
        await _sut.RemoveAsync(key);   // gen → 2

        var gen = await _sut.GetGenerationAsync(key);
        gen.Should().Be(2);
    }

    [Fact]
    public async Task Given_ShortTtl_WhenTtlExpires_Then_GetReturnsNull()
    {
        var memCache = new MemoryCache(new MemoryCacheOptions());
        var settings = Options.Create(new CacheSettings { ProductTtlMinutes = 1.0 / 600.0 }); // ~100ms
        var sut = new MemoryProductCache(memCache, settings, NullLogger<MemoryProductCache>.Instance);

        var key     = "product:ttl";
        var product = new Product { Id = 1, Name = "Expiry Test", Price = 99m, Version = 1 };

        await sut.SetAsync(key, product, expectedGeneration: 0L);
        await Task.Delay(200);
        var cached = await sut.GetAsync(key);

        cached.Should().BeNull();
    }
}
