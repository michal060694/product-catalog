using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductCatalog.Domain.Entities;
using ProductCatalog.Infrastructure.Cache;
using ProductCatalog.Infrastructure.TaskStore;

namespace ProductCatalog.Tests.StaleDataExamples;

public class CoalescingTests
{
    private readonly SharedTaskStore _sut;

    public CoalescingTests()
    {
        var settings = Options.Create(new CacheSettings { InFlightTimeoutSeconds = 30 });
        _sut = new SharedTaskStore(NullLogger<SharedTaskStore>.Instance, settings);
    }

    [Fact]
    public async Task Given_10ConcurrentRequests_SameUncachedKey_WhenFactoryIsInFlight_ThenFactoryCalledOnce()
    {
        // Scenario (Cache Stampede):
        //   Cache = MISS for all 10 threads
        //   10 × Thread → GetOrAddAsync(key, factory)
        //   Without coalescing → 10 repository calls
        //   With SharedTaskStore  → 1 factory call; all 10 threads await the same Task
        //   Solution: ConcurrentDictionary<string, Lazy<Task<Product?>>>
        //             The first caller creates the Lazy; every subsequent caller gets the
        //             existing Lazy and awaits its already-running Task.
        const string key = "product:1";
        const int threads = 10;
        var product = new Product { Id = 1, Name = "Product 1", Price = 10m, Version = 1 };
        var callCount = 0;
        var tcs = new TaskCompletionSource<Product?>();

        // Factory that counts how many times it is invoked
        Func<Task<Product?>> factory = () =>
        {
            Interlocked.Increment(ref callCount);
            return tcs.Task; // stays in-flight until tcs.SetResult
        };

        // Each thread starts 100 ms after the previous one.
        // tcs.Task stays in-flight the whole time, so every late-arriving thread still
        // finds the existing Lazy and awaits the same Task — no second factory call.
        var tasks = Enumerable.Range(0, threads)
            .Select(i => Task.Run(async () =>
            {
                await Task.Delay(i * 100); // thread 0 → 0 ms, thread 1 → 500 ms, …, thread 9 → 4500 ms
                return await _sut.GetOrAddAsync(key, factory);
            }))
            .ToArray();

        // Wait until the last thread has called GetOrAddAsync, then resolve
        await Task.Delay((threads - 1) * 100 + 200); // all threads are now blocked on tcs.Task

        // Resolve the single in-flight task — all 10 threads should receive this product
        tcs.SetResult(product);

        var results = await Task.WhenAll(tasks);

        callCount.Should().Be(1, "SharedTaskStore must coalesce all concurrent misses into a single factory call");
        results.Should().AllSatisfy(r => r.Should().BeSameAs(product, "every thread must receive the same product instance"));
    }
}
