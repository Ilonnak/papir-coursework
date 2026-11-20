using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Route("api/cart")]
[Authorize]
public class CartController : ApiControllerBase
{
    private readonly CartService _cartService;

    public CartController(CartService cartService)
    {
        _cartService = cartService;
    }

    [HttpGet]
    public async Task<IActionResult> GetCart()
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return Unauthorized(new { message = "Необхідно увійти до облікового запису." });

        var cart = await _cartService.GetCartAsync(userId.Value);
        return Ok(cart);
    }

    [HttpPost("items")]
    public async Task<IActionResult> AddItem([FromBody] AddToCartRequestDto dto)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return Unauthorized(new { message = "Необхідно увійти до облікового запису." });

        var result = await _cartService.AddItemAsync(userId.Value, dto);
        return ToActionResult(result);
    }

    [HttpPut("items/{productId:int}")]
    public async Task<IActionResult> UpdateQuantity(int productId, [FromBody] UpdateCartItemRequestDto dto)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return Unauthorized(new { message = "Необхідно увійти до облікового запису." });

        var result = await _cartService.UpdateItemQuantityAsync(userId.Value, productId, dto);
        return ToActionResult(result);
    }

    [HttpDelete("items/{productId:int}")]
    public async Task<IActionResult> RemoveItem(int productId)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return Unauthorized(new { message = "Необхідно увійти до облікового запису." });

        var result = await _cartService.RemoveItemAsync(userId.Value, productId);
        return ToActionResult(result);
    }
}
