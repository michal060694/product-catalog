using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductCatalog.Domain;
using ProductCatalog.Infrastructure;

namespace ProductCatalog.Tests;

public class StaleCacheWriteTests
{
    private readonly MemoryProductCache _sut;

    public StaleCacheWriteTests()
    {
        var memCache = new MemoryCache(new MemoryCacheOptions());
        var settings = Options.Create(new CacheSettings { ProductTtlMinutes = 5 });
        _sut = new MemoryProductCache(memCache, settings, NullLogger<MemoryProductCache>.Instance);
    }

    [Fact]
    public async Task Given_ThreadA_GetInFlight_WhenThreadB_PutInvalidates_Then_ThreadA_StaleWriteIsRejected()
    {
        // Scenario:
        //   Cache = empty
        //   Thread A → GET → gen = GetGenerationAsync()   (gen=0)
        //                     Repository.Get(id)          (returns V1, still in flight…)
        //   Thread B → PUT → Repository.Update(V2)
        //                     Cache.RemoveAsync()         (gen → 1)
        //   Thread A ←       Repository returns V1
        //                     Cache.SetAsync(V1, gen=0)   ← 0 ≠ 1 → REJECTED
        //   Result: Cache = empty, NOT stale V1
        //   solution: Generation Counter
        //             Before Cache.Set(), verify that no update
        //             occurred while the GET was in progress.
        var key   = "product:1";
        var staleV1 = new Product { Id = 1, Name = "Old Name", Price = 100m, Version = 1 };

        // Thread A: captures generation before going to the repository
        var gen = await _sut.GetGenerationAsync(key); // gen = 0

        // Thread B races in: PUT updates the DB and invalidates the cache
        await _sut.RemoveAsync(key); // gen → 1

        // Thread A: returns from the repository and tries to write stale V1
        await _sut.SetAsync(key, staleV1, gen); // 0 ≠ 1 → rejected by generation guard

        // Cache must be empty — stale V1 must NOT have been written
        var cached = await _sut.GetAsync(key);
        cached.Should().BeNull("the generation guard must reject a stale write that arrived after a PUT invalidation");
    }




}
