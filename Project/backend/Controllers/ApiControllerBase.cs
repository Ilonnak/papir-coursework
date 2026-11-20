using System.Security.Claims;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected int? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(claim, out var id) ? id : null;
    }

    protected bool IsCurrentUserAdmin()
    {
        return User.IsInRole("Admin") ||
               string.Equals(User.FindFirstValue("is_admin"), "true", StringComparison.OrdinalIgnoreCase);
    }

    protected IActionResult ToActionResult<T>(ServiceResult<T> result)
    {
        if (result.Success)
        {
            return StatusCode(result.StatusCode, result.Data);
        }

        return StatusCode(result.StatusCode, new
        {
            message = result.ErrorMessage ?? "Виникла помилка під час виконання запиту.",
            code = result.ErrorCode,
            data = result.Data
        });
    }
}
