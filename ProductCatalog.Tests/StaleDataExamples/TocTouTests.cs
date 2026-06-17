using AutoMapper;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductCatalog.Application.DTOs;
using ProductCatalog.Application.Mappings;
using ProductCatalog.Application.Services;
using ProductCatalog.Domain.Cache;
using ProductCatalog.Domain.Entities;
using ProductCatalog.Domain.Repositories;
using ProductCatalog.Infrastructure.Cache;
using ProductCatalog.Infrastructure.TaskStore;

namespace ProductCatalog.Tests.StaleDataExamples;

public class TocTouTests
{
    private readonly MemoryProductCache _cache;
    private readonly SharedTaskStore _taskStore;
    private readonly IMapper _mapper;

    public TocTouTests()
    {
        var settings = Options.Create(new CacheSettings { ProductTtlMinutes = 5, InFlightTimeoutSeconds = 30 });
        _cache     = new MemoryProductCache(new MemoryCache(new MemoryCacheOptions()), settings, NullLogger<MemoryProductCache>.Instance);
        _taskStore = new SharedTaskStore(NullLogger<SharedTaskStore>.Instance, settings);
        _mapper    = new ServiceCollection()
                         .AddLogging()
                         .AddAutoMapper(cfg => cfg.AddProfile<ProductProfile>())
                         .BuildServiceProvider()
                         .GetRequiredService<IMapper>();
    }

    [Fact]
    public async Task Given_NConcurrentCacheMisses_WhenRepoIsInFlight_ThenRepoCalledOnce_AndCacheWrittenOnce()
    {
        // Scenario (TOCTOU — Time Of Check To Time Of Use):
        //
        //   WITHOUT per-key lock:
        //     Thread A: Cache.Get() → MISS  [check]
        //     Thread B: Cache.Get() → MISS  [check]
        //     Thread A: Repository.Get()    ← duplicate call!
        //     Thread B: Repository.Get()    ← duplicate call!
        //     Thread A: Cache.Set()         ← duplicate write!
        //     Thread B: Cache.Set()         ← duplicate write!
        //
        //   WITH per-key lock (SharedTaskStore):
        //     Thread A: Cache.Get() → MISS → creates Lazy<Task> → factory runs → repo BLOCKS
        //     Thread B–J: Cache.Get() → MISS → finds existing Lazy → awaits same Task
        //     gate opens → factory completes → 1 repo call, 1 cache write
        //     All threads receive the same result.
        const int threads = 10;
        const int productId = 1;
        var key = CacheKeys.ForProduct(productId);

        // Gate: keeps the repository call in-flight so all threads register before it completes.
        // Without this, the factory could finish before threads 2-N reach GetOrAddAsync,
        // causing them to create a NEW in-flight entry → multiple repo calls → TOCTOU.
        var gate         = new SemaphoreSlim(0, 1);
        var repoCallCount = 0;

        var repo = A.Fake<IProductRepository>();
        A.CallTo(() => repo.GetById(productId))
            .ReturnsLazily(() =>
            {
                Interlocked.Increment(ref repoCallCount);
                gate.Wait(); // blocks exactly one thread pool thread inside the Lazy factory
                return new Product { Id = productId, Name = "Product 1", Price = 10m, Stock = 5, Version = 1 };
            });

        var sut = new ProductService(repo, _cache, _taskStore, _mapper, NullLogger<ProductService>.Instance);

        // All threads start together so they all race to the cache miss at the same time
        var barrier = new Barrier(threads);
        var tasks = Enumerable.Range(0, threads)
            .Select(_ => Task.Run(async () =>
            {
                barrier.SignalAndWait(); // all threads enter GetProductAsync simultaneously
                return await sut.GetProductAsync(productId);
            }))
            .ToArray();

        // Wait for all threads to have called GetOrAddAsync and for Thread A to be blocking at repo
        await Task.Delay(200);

        // Release the gate: factory finishes → cache written once → all threads receive the result
        gate.Release();

        var results = await Task.WhenAll(tasks);

        // Per-key lock (SharedTaskStore) must guarantee exactly one repo call — no TOCTOU duplicates
        repoCallCount.Should().Be(1,
            "SharedTaskStore coalesces all concurrent cache misses into a single factory call — " +
            "the per-key lock prevents TOCTOU (duplicate repository calls and duplicate cache writes)");

        // Cache must contain exactly one entry with the correct version
        var cached = await _cache.GetAsync(key);
        cached.Should().NotBeNull();
        cached!.Version.Should().Be(1, "the product must be written to cache exactly once");

        // All threads must have received the same result
        results.Should().AllSatisfy(r => r.Name.Should().Be("Product 1",
            "every thread must receive the result of the single repository call"));
    }
}
