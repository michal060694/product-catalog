using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Application;

namespace ProductCatalog.Api;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _service;

    public ProductsController(IProductService service) => _service = service;

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetByIdAsync(int id, CancellationToken ct)
        => Ok(await _service.GetProductAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CreateProductDto dto, CancellationToken ct)
    {
        var created = await _service.CreateProductAsync(dto, ct);
        return CreatedAtAction(nameof(GetByIdAsync), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAsync(int id, [FromBody] UpdateProductDto dto, CancellationToken ct)
        => Ok(await _service.UpdateProductAsync(id, dto, ct));
}
