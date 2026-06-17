namespace ProductCatalog.Domain;

public interface ISharedTaskStore
{
    Task<Product?> GetOrAddAsync(string key, Func<Task<Product?>> factory);
}
