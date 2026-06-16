using ProductCatalog.Application.DTOs;

namespace ProductCatalog.Application.Services;

public interface IProductService
{
    Task<ProductDto?> GetProductAsync(int id, CancellationToken ct = default);
    Task<ProductDto> CreateProductAsync(CreateProductDto dto, CancellationToken ct = default);
    Task<ProductDto?> UpdateProductAsync(int id, UpdateProductDto dto, CancellationToken ct = default);
}
