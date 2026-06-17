namespace ProductCatalog.Domain;

public interface IProductRepository
{
    Product? GetById(int id);
    void Add(Product product);
    void Update(Product product);
}
