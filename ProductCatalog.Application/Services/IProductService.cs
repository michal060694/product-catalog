using ProductCatalog.Application.DTOs;

namespace ProductCatalog.Application.Services;

public interface IProductService
{
    Task<ProductDto?> GetProductAsync(int id, CancellationToken ct = default);
}
