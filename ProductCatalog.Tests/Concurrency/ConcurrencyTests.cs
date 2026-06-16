using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ProductCatalog.Domain.Entities;
using ProductCatalog.Infrastructure.TaskStore;

namespace ProductCatalog.Tests.Concurrency;

public class ConcurrencyTests
{
    private readonly SharedTaskStore _sut = new(NullLogger<SharedTaskStore>.Instance);

    [Fact]
    public async Task Given_100ConcurrentRequests_WhenCacheMiss_Then_FactoryCalledOnce()
    {
        var product = new Product { Id = 1, Name = "Laptop", Price = 4999m, Version = 1 };
        int factoryCallCount = 0;
        var tcs = new TaskCompletionSource<Product?>();

        Func<Task<Product?>> factory = () =>
        {
            Interlocked.Increment(ref factoryCallCount);
            return tcs.Task;
        };

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => _sut.GetOrAddAsync("product:1", factory))
            .ToArray();

        tcs.SetResult(product);
        var results = await Task.WhenAll(tasks);

        factoryCallCount.Should().Be(1);
        results.Should().AllSatisfy(r => r.Should().BeSameAs(product));
    }

    [Fact]
    public async Task Given_CompletedInFlightTask_WhenRequestedAgain_Then_NewFactoryInvoked()
    {
        var product = new Product { Id = 2, Name = "Keyboard", Price = 349m, Version = 1 };
        int firstCount  = 0;
        int secondCount = 0;

        await _sut.GetOrAddAsync("product:2", () =>
        {
            firstCount++;
            return Task.FromResult<Product?>(product);
        });

        // Wait for ContinueWith to remove the completed entry
        await Task.Delay(20);

        await _sut.GetOrAddAsync("product:2", () =>
        {
            secondCount++;
            return Task.FromResult<Product?>(product);
        });

        firstCount.Should().Be(1);
        secondCount.Should().Be(1, "entry was removed after first task completed");
    }

    [Fact]
    public async Task Given_DifferentKeys_WhenConcurrentRequests_Then_FactoryCalledOncePerKey()
    {
        var product1 = new Product { Id = 1, Name = "Laptop",   Version = 1 };
        var product2 = new Product { Id = 2, Name = "Keyboard", Version = 1 };
        int count1 = 0, count2 = 0;

        var tcs1 = new TaskCompletionSource<Product?>();
        var tcs2 = new TaskCompletionSource<Product?>();

        var tasks1 = Enumerable.Range(0, 50)
            .Select(_ => _sut.GetOrAddAsync("product:1", () => { Interlocked.Increment(ref count1); return tcs1.Task; }))
            .ToArray();

        var tasks2 = Enumerable.Range(0, 50)
            .Select(_ => _sut.GetOrAddAsync("product:2", () => { Interlocked.Increment(ref count2); return tcs2.Task; }))
            .ToArray();

        tcs1.SetResult(product1);
        tcs2.SetResult(product2);
        await Task.WhenAll(tasks1.Concat(tasks2));

        count1.Should().Be(1);
        count2.Should().Be(1);
    }
}
