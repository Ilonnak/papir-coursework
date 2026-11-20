using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Route("api")]
public class CatalogController : ApiControllerBase
{
    private readonly CatalogService _catalogService;

    public CatalogController(CatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet("categories")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await _catalogService.GetCategoriesAsync(includeInactiveCounts: false);
        return Ok(categories);
    }

    [HttpGet("products")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCatalog([FromQuery] CatalogQueryDto query)
    {
        var result = await _catalogService.GetPublicCatalogAsync(query);
        return ToActionResult(result);
    }

    [HttpGet("products/{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetProduct(int id)
    {
        var result = await _catalogService.GetPublicProductByIdAsync(id);
        return ToActionResult(result);
    }
}
