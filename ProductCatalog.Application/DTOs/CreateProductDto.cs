namespace ProductCatalog.Application;

public record CreateProductDto(string Name, decimal Price, int Stock);
