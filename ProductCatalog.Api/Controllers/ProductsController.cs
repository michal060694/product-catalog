using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Application.DTOs;
using ProductCatalog.Application.Services;

namespace ProductCatalog.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _service;

    public ProductsController(IProductService service) => _service = service;

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetByIdAsync(int id, CancellationToken ct)
    {
        if (id <= 0)
            return BadRequest(new { message = "Id must be a positive integer." });

        var dto = await _service.GetProductAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CreateProductDto dto, CancellationToken ct)
    {
        var created = await _service.CreateProductAsync(dto, ct);
        return CreatedAtAction(nameof(GetByIdAsync), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAsync(int id, [FromBody] UpdateProductDto dto, CancellationToken ct)
    {
        if (id <= 0)
            return BadRequest(new { message = "Id must be a positive integer." });

        var result = await _service.UpdateProductAsync(id, dto, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
