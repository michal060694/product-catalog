using System.Collections.Concurrent;
using FluentAssertions;
using ProductCatalog.Domain;
using ProductCatalog.Infrastructure;

namespace ProductCatalog.Tests;

public class ConcurrentDictionaryTests
{
    [Fact]
    public async Task Given_ConcurrentAddReadUpdate_ThenNoConcurrencyExceptions_AndSeedDataRemainsConsistent()
    {
        // Scenario (Concurrent Dictionary Access):
        //   Thread A → Add      → _store[newId] = product
        //   Thread B → Read     → _store.GetValueOrDefault(id)
        //   Thread C → Update   → _store[id]    = updated product
        //
        //   WITHOUT ConcurrentDictionary (plain Dictionary<TKey,TValue>):
        //     - InvalidOperationException: "Collection was modified; enumeration operation may not execute."
        //     - NullReferenceException / KeyNotFoundException under concurrent resize
        //     - Silent data corruption: two writers race on the same bucket
        //
        //   WITH ConcurrentDictionary<TKey,TValue>:
        //     - Each operation is atomic — no locks needed by the caller
        //     - Striped locking internally; reads never block each other
        //     - No exceptions, no data corruption, consistent state guaranteed
        const int operationsPerThread = 200;

        var repo = new InMemoryProductRepository();
        var exceptions = new ConcurrentBag<Exception>();

        // Thread A: Add 200 new products while B and C are running
        var addThread = Task.Run(() =>
        {
            for (var i = 0; i < operationsPerThread; i++)
            {
                try
                {
                    repo.Add(new Product { Name = $"New {i}", Price = 10m, Stock = i });
                    Task.Delay(i * 10000);
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        });

        // Thread B: Read the 3 seed products while A is adding and C is updating
        var readThread = Task.Run(() =>
        {
            for (var i = 0; i < operationsPerThread; i++)
            {
                try
                {
                    _ = repo.GetById(i % 3 + 1);
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        });

        // Thread C: Update seed products while A is adding and B is reading
        var updateThread = Task.Run(() =>
        {
            for (var i = 0; i < operationsPerThread; i++)
            {
                try
                {
                    var p = repo.GetById(i % 3 + 1);
                    if (p is not null)
                    {
                        p.Price = 999m + i;
                        repo.Update(p);
                    }
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        });

        await Task.WhenAll(addThread, readThread, updateThread);

        exceptions.Should().BeEmpty(
            "ConcurrentDictionary<TKey,TValue> handles concurrent Add / Read / Update atomically — " +
            "a plain Dictionary<TKey,TValue> would throw InvalidOperationException or corrupt data under the same load");

        // All three seed products must still be accessible after 600 concurrent operations
        repo.GetById(1).Should().NotBeNull("seed product 1 must survive concurrent access");
        repo.GetById(2).Should().NotBeNull("seed product 2 must survive concurrent access");
        repo.GetById(3).Should().NotBeNull("seed product 3 must survive concurrent access");
    }
}
