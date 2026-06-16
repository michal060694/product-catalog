using ProductCatalog.Domain.Entities;

namespace ProductCatalog.Domain.TaskStore;

public interface ISharedTaskStore
{
    Task<Product?> GetOrAddAsync(string key, Func<Task<Product?>> factory);
}
