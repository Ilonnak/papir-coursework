using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ApiControllerBase
{
    private readonly CatalogService _catalogService;
    private readonly OrderService _orderService;
    private readonly ImageStorageService _imageStorageService;

    public AdminController(
        CatalogService catalogService,
        OrderService orderService,
        ImageStorageService imageStorageService)
    {
        _catalogService = catalogService;
        _orderService = orderService;
        _imageStorageService = imageStorageService;
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await _catalogService.GetCategoriesAsync(includeInactiveCounts: true);
        return Ok(categories);
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] SaveCategoryRequestDto dto)
    {
        var result = await _catalogService.CreateCategoryAsync(dto);
        return ToActionResult(result);
    }

    [HttpPut("categories/{id:int}")]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] SaveCategoryRequestDto dto)
    {
        var result = await _catalogService.UpdateCategoryAsync(id, dto);
        return ToActionResult(result);
    }

    [HttpDelete("categories/{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var result = await _catalogService.DeleteCategoryAsync(id);
        return ToActionResult(result);
    }

    [HttpGet("products")]
    public async Task<IActionResult> GetProducts(
        [FromQuery] string? search,
        [FromQuery] string? visibility,
        [FromQuery] int? categoryId)
    {
        var products = await _catalogService.GetAdminProductsAsync(search, visibility, categoryId);
        return Ok(products);
    }

    [HttpPost("products")]
    public async Task<IActionResult> CreateProduct([FromBody] SaveProductRequestDto dto)
    {
        var result = await _catalogService.CreateProductAsync(dto);
        return ToActionResult(result);
    }

    [HttpPut("products/{id:int}")]
    public async Task<IActionResult> UpdateProduct(int id, [FromBody] SaveProductRequestDto dto)
    {
        var result = await _catalogService.UpdateProductAsync(id, dto);
        return ToActionResult(result);
    }

    public class ToggleVisibilityDto
    {
        public bool IsActive { get; set; }
    }

    [HttpPatch("products/{id:int}/visibility")]
    public async Task<IActionResult> ToggleProductVisibility(int id, [FromBody] ToggleVisibilityDto dto)
    {
        var result = await _catalogService.ToggleVisibilityAsync(id, dto.IsActive);
        return ToActionResult(result);
    }

    [HttpPost("products/upload-image")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadProductImage(IFormFile? file)
    {
        var result = await _imageStorageService.SaveProductImageAsync(file);
        if (!result.Success)
        {
            return ToActionResult(result);
        }

        return StatusCode(201, new { imagePath = result.Data });
    }

    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders([FromQuery] string? search, [FromQuery] string? status)
    {
        var orders = await _orderService.GetAdminOrdersAsync(search, status);
        return Ok(orders);
    }

    [HttpGet("orders/{id:int}")]
    public async Task<IActionResult> GetOrderById(int id)
    {
        var result = await _orderService.GetAdminOrderByIdAsync(id);
        return ToActionResult(result);
    }

    [HttpPatch("orders/{id:int}/status")]
    public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] UpdateOrderStatusRequestDto dto)
    {
        var result = await _orderService.UpdateOrderStatusAsync(id, dto);
        return ToActionResult(result);
    }

    [HttpPatch("orders/{id:int}/payment")]
    public async Task<IActionResult> MarkOrderPaid(int id)
    {
        var result = await _orderService.MarkOrderPaidAsync(id);
        return ToActionResult(result);
    }
}
