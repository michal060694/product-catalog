using ProductCatalog.Domain.Entities;

namespace ProductCatalog.Domain.Repositories;

public interface IProductRepository
{
    Product? GetById(int id);
    void Add(Product product);
    void Update(Product product);
}
