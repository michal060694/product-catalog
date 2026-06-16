namespace ProductCatalog.Domain.Entities;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal CostPrice { get; set; }   // מחיר עלות — סודי, לא יוצא לDTO/Cache
    public int Stock { get; set; }
    public int Version { get; set; }
}
