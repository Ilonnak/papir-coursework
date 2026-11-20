using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Backend.Data;
using Backend.DTOs;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class CartService
{
    private readonly AppDbContext _db;

    public CartService(AppDbContext db)
    {
        _db = db;
    }

    public static string ComputeCartSnapshotToken(IEnumerable<(int ProductId, int Quantity, decimal UnitPrice)> items)
    {
        var canonical = string.Join(";",
            items.OrderBy(i => i.ProductId)
                 .Select(i => $"{i.ProductId}:{i.Quantity}:{Math.Round(i.UnitPrice, 2).ToString("0.00", CultureInfo.InvariantCulture)}"));

        if (string.IsNullOrEmpty(canonical))
        {
            return string.Empty;
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hashBytes);
    }

    public async Task<CartResponseDto> GetCartAsync(int userId)
    {
        var items = await _db.CartItems
            .AsNoTracking()
            .Include(ci => ci.Product)
                .ThenInclude(p => p!.Category)
            .Where(ci => ci.UserId == userId)
            .OrderBy(ci => ci.Product!.Name)
            .ToListAsync();

        var dtoItems = new List<CartItemDto>();
        var warnings = new List<string>();

        foreach (var ci in items)
        {
            if (ci.Product == null) continue;

            string? issue = null;
            if (!ci.Product.IsActive)
            {
                issue = $"Товар «{ci.Product.Name}» приховано з каталогу й недоступний для оформлення.";
                warnings.Add(issue);
            }
            else if (ci.Product.StockQuantity <= 0)
            {
                issue = $"Товар «{ci.Product.Name}» закінчився на складі.";
                warnings.Add(issue);
            }
            else if (ci.Quantity > ci.Product.StockQuantity)
            {
                issue = $"Для товару «{ci.Product.Name}» доступно лише {ci.Product.StockQuantity} шт. (у кошику {ci.Quantity} шт.). Зменшіть кількість.";
                warnings.Add(issue);
            }

            dtoItems.Add(new CartItemDto
            {
                ProductId = ci.ProductId,
                ProductName = ci.Product.Name,
                CategoryName = ci.Product.Category?.Name ?? string.Empty,
                ImagePath = string.IsNullOrWhiteSpace(ci.Product.ImagePath) ? "/images/products/placeholder.svg" : ci.Product.ImagePath,
                UnitPrice = ci.Product.Price,
                Quantity = ci.Quantity,
                AvailableStock = ci.Product.StockQuantity,
                IsProductActive = ci.Product.IsActive,
                IssueDescription = issue
            });
        }

        var totalQuantity = dtoItems.Sum(i => i.Quantity);
        var totalAmount = dtoItems.Sum(i => i.LineTotal);
        var canCheckout = dtoItems.Count > 0 && dtoItems.All(i => !i.HasIssue);
        var snapshotToken = ComputeCartSnapshotToken(dtoItems.Select(i => (i.ProductId, i.Quantity, i.UnitPrice)));

        return new CartResponseDto
        {
            Items = dtoItems,
            TotalQuantity = totalQuantity,
            TotalAmount = totalAmount,
            CanCheckout = canCheckout,
            CartSnapshotToken = snapshotToken,
            Warnings = warnings
        };
    }

    public async Task<ServiceResult<CartResponseDto>> AddItemAsync(int userId, AddToCartRequestDto dto)
    {
        if (dto.Quantity <= 0)
        {
            return ServiceResult<CartResponseDto>.Fail("Кількість товару повинна бути додатним цілим числом (від 1).");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        // Єдиний транзакційний порядок блокування для всіх змін кошика та оформлення одного користувача:
        // спочатку блокуємо рядок users FOR UPDATE.
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id = {userId} FOR UPDATE");

        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.ProductId);
        if (product == null || !product.IsActive)
        {
            await transaction.RollbackAsync();
            return ServiceResult<CartResponseDto>.Fail("Товар не знайдено або він недоступний у каталозі.", 404, "PRODUCT_NOT_AVAILABLE");
        }

        if (product.StockQuantity <= 0)
        {
            await transaction.RollbackAsync();
            return ServiceResult<CartResponseDto>.Fail("Цього товару немає в наявності.", 409, "OUT_OF_STOCK");
        }

        var existing = await _db.CartItems.FirstOrDefaultAsync(ci => ci.UserId == userId && ci.ProductId == dto.ProductId);
        var targetQuantity = (existing?.Quantity ?? 0) + dto.Quantity;

        if (targetQuantity > product.StockQuantity)
        {
            await transaction.RollbackAsync();
            return ServiceResult<CartResponseDto>.Fail(
                $"На складі доступно лише {product.StockQuantity} шт. товару «{product.Name}» (у кошику вже {existing?.Quantity ?? 0} шт.).",
                409,
                "INSUFFICIENT_STOCK");
        }

        if (existing == null)
        {
            _db.CartItems.Add(new CartItem
            {
                UserId = userId,
                ProductId = dto.ProductId,
                Quantity = dto.Quantity,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Quantity = targetQuantity;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var updatedCart = await GetCartAsync(userId);
        return ServiceResult<CartResponseDto>.Ok(updatedCart);
    }

    public async Task<ServiceResult<CartResponseDto>> UpdateItemQuantityAsync(int userId, int productId, UpdateCartItemRequestDto dto)
    {
        if (dto.Quantity <= 0)
        {
            return ServiceResult<CartResponseDto>.Fail("Кількість товару повинна бути додатним цілим числом (від 1).");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        // Блокуємо рядок users FOR UPDATE для серіалізації з оформленням та іншими змінами кошика
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id = {userId} FOR UPDATE");

        var existing = await _db.CartItems.FirstOrDefaultAsync(ci => ci.UserId == userId && ci.ProductId == productId);
        if (existing == null)
        {
            await transaction.RollbackAsync();
            return ServiceResult<CartResponseDto>.Fail("Позицію в кошику не знайдено.", 404);
        }

        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null || !product.IsActive)
        {
            await transaction.RollbackAsync();
            return ServiceResult<CartResponseDto>.Fail("Товар приховано з каталогу або видалено. Видаліть його з кошика.", 409, "PRODUCT_INACTIVE");
        }

        if (product.StockQuantity <= 0)
        {
            await transaction.RollbackAsync();
            return ServiceResult<CartResponseDto>.Fail("Товар закінчився на складі. Видаліть його з кошика.", 409, "OUT_OF_STOCK");
        }

        if (dto.Quantity > product.StockQuantity)
        {
            await transaction.RollbackAsync();
            return ServiceResult<CartResponseDto>.Fail(
                $"Максимально доступна кількість для товару «{product.Name}» — {product.StockQuantity} шт.",
                409,
                "INSUFFICIENT_STOCK");
        }

        existing.Quantity = dto.Quantity;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var updatedCart = await GetCartAsync(userId);
        return ServiceResult<CartResponseDto>.Ok(updatedCart);
    }

    public async Task<ServiceResult<CartResponseDto>> RemoveItemAsync(int userId, int productId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();

        // Блокуємо рядок users FOR UPDATE для серіалізації з оформленням та іншими змінами кошика
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id = {userId} FOR UPDATE");

        var existing = await _db.CartItems.FirstOrDefaultAsync(ci => ci.UserId == userId && ci.ProductId == productId);
        if (existing != null)
        {
            _db.CartItems.Remove(existing);
            await _db.SaveChangesAsync();
        }

        await transaction.CommitAsync();

        var updatedCart = await GetCartAsync(userId);
        return ServiceResult<CartResponseDto>.Ok(updatedCart);
    }
}
