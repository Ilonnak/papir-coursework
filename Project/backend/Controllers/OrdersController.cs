using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Route("api/orders")]
[Authorize]
public class OrdersController : ApiControllerBase
{
    private readonly OrderService _orderService;

    public OrdersController(OrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpGet("pickup-info")]
    [AllowAnonymous]
    public IActionResult GetPickupInfo()
    {
        return Ok(_orderService.GetPickupInfo());
    }

    [HttpGet]
    public async Task<IActionResult> GetMyOrders()
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return Unauthorized(new { message = "Необхідно увійти до облікового запису." });

        var orders = await _orderService.GetUserOrdersAsync(userId.Value);
        return Ok(orders);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetMyOrderById(int id)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return Unauthorized(new { message = "Необхідно увійти до облікового запису." });

        var result = await _orderService.GetUserOrderByIdAsync(userId.Value, id);
        return ToActionResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequestDto dto)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return Unauthorized(new { message = "Необхідно увійти до облікового запису." });

        var result = await _orderService.CreateOrderAsync(userId.Value, dto);
        return ToActionResult(result);
    }
}
