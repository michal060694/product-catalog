using Microsoft.AspNetCore.Mvc;
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
}
