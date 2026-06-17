namespace ProductCatalog.Domain;

public interface IProductCache
{
    Task<Product?> GetAsync(string key, CancellationToken ct = default);
    Task<long> GetGenerationAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, Product product, long expectedGeneration, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}
