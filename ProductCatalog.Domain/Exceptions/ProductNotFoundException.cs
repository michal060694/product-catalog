namespace ProductCatalog.Domain;

public class ProductNotFoundException : Exception
{
    public ProductNotFoundException(int id)
        : base($"Product with Id {id} was not found.") { }
}
