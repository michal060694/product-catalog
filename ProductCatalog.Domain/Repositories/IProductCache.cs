using ProductCatalog.Domain.Entities;

namespace ProductCatalog.Domain.Repositories;

public interface IProductCache
{
    Task<Product?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, Product product, CancellationToken ct = default);
    Task RemoveAsync(string key, int minimumVersionFloor = 0, CancellationToken ct = default);
}
