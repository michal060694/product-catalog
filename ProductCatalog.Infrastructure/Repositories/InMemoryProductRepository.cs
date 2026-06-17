using System.Collections.Concurrent;
using ProductCatalog.Domain;

namespace ProductCatalog.Infrastructure;

public class InMemoryProductRepository : IProductRepository
{
    private readonly ConcurrentDictionary<int, Product> _store = new();
    private int _nextId = 3;  // seed data occupies 1-3

    public InMemoryProductRepository()
    {
        //test data
        _store[1] = new Product { Id = 1, Name = "Laptop",   Price = 4999.99m, CostPrice = 3200m, Stock = 10 };
        _store[2] = new Product { Id = 2, Name = "Keyboard", Price = 349.90m,  CostPrice = 120m,  Stock = 50 };
        _store[3] = new Product { Id = 3, Name = "Mouse",    Price = 199.00m,  CostPrice = 65m,   Stock = 75 };
    }

    public Product? GetById(int id) => _store.GetValueOrDefault(id);

    public void Add(Product p)
    {
        p.Id = Interlocked.Increment(ref _nextId);  // returns 4, 5, 6…
        _store[p.Id] = p;
    }

    public void Update(Product p)   => _store[p.Id] = p;
}
