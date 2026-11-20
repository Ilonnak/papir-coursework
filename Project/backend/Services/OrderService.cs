using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Backend.Configuration;
using Backend.Data;
using Backend.DTOs;
using Backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Services;

public class OrderService
{
    private readonly AppDbContext _db;
    private readonly PickupPointSettings _pickupSettings;

    private static readonly Dictionary<string, List<string>> AllowedStatusTransitions = new()
    {
        [OrderStatus.New] = new List<string> { OrderStatus.Processing, OrderStatus.Cancelled },
        [OrderStatus.Processing] = new List<string> { OrderStatus.Completed, OrderStatus.Cancelled },
        [OrderStatus.Completed] = new List<string>(),
        [OrderStatus.Cancelled] = new List<string>()
    };

    public OrderService(AppDbContext db, IOptions<PickupPointSettings> pickupOptions)
    {
        _db = db;
        _pickupSettings = pickupOptions.Value;
    }

    public PickupInfoDto GetPickupInfo() => new()
    {
        Title = _pickupSettings.Title,
        Address = _pickupSettings.Address,
        Schedule = _pickupSettings.Schedule,
        PaymentNote = _pickupSettings.PaymentNote
    };

    public async Task<ServiceResult<OrderDto>> CreateOrderAsync(int userId, CreateOrderRequestDto dto)
    {
        var firstName = dto.RecipientFirstName?.Trim() ?? string.Empty;
        var lastName = dto.RecipientLastName?.Trim() ?? string.Empty;
        var phone = dto.RecipientPhone?.Trim() ?? string.Empty;
        var email = dto.RecipientEmail?.Trim() ?? string.Empty;
        var comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim();
        var idempotencyKey = dto.IdempotencyKey?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) ||
            string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(email))
        {
            return ServiceResult<OrderDto>.Fail("Заповніть усі обов’язкові контакти отримувача (ім’я, прізвище, телефон та email).");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length < 4)
        {
            return ServiceResult<OrderDto>.Fail("Необхідно передати непорожній ключ ідемпотентності (IdempotencyKey).", 400, "IDEMPOTENCY_KEY_REQUIRED");
        }

        var clientConfirmedToken = ResolveClientConfirmationToken(dto);
        if (string.IsNullOrWhiteSpace(clientConfirmedToken))
        {
            return ServiceResult<OrderDto>.Fail(
                "Необхідно підтвердити актуальний склад кошика та ціни позицій (CartSnapshotToken або ConfirmedItems).",
                400,
                "CART_CONFIRMATION_REQUIRED");
        }

        var requestPayloadHash = ComputeIdempotencyPayloadHash(
            firstName, lastName, phone, email, comment, clientConfirmedToken, dto.ExpectedTotalAmount);

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // Блокуємо рядок користувача у транзакції (єдиний порядок блокування з усіма мутаціями кошика)
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id = {userId} FOR UPDATE");

            // Перевіряємо, чи вже виконувався запит із цим IdempotencyKey для цього користувача
            var existingOrder = await GetOrderQuery()
                .FirstOrDefaultAsync(o => o.UserId == userId && o.IdempotencyKey == idempotencyKey);

            if (existingOrder != null)
            {
                if (!string.IsNullOrEmpty(existingOrder.IdempotencyPayloadHash) &&
                    !string.Equals(existingOrder.IdempotencyPayloadHash, requestPayloadHash, StringComparison.Ordinal))
                {
                    await transaction.RollbackAsync();
                    return ServiceResult<OrderDto>.Fail(
                        "Ключ ідемпотентності вже було використано для іншого запиту з відмінними параметрами отримувача або складом замовлення.",
                        409,
                        "IDEMPOTENCY_KEY_PAYLOAD_MISMATCH");
                }

                await transaction.CommitAsync();
                return ServiceResult<OrderDto>.Ok(MapOrder(existingOrder), 200);
            }

            var cartItems = await _db.CartItems
                .Where(ci => ci.UserId == userId)
                .OrderBy(ci => ci.ProductId)
                .ToListAsync();

            if (cartItems.Count == 0)
            {
                await transaction.RollbackAsync();
                return ServiceResult<OrderDto>.Fail("Ваш кошик порожній. Додайте товари перед оформленням замовлення.", 400, "EMPTY_CART");
            }

            // Блокуємо рядки товарів у детермінованому порядку ProductId
            var lockedProducts = new Dictionary<int, Product>();
            foreach (var ci in cartItems)
            {
                var p = await _db.Products
                    .FromSqlInterpolated($"SELECT * FROM products WHERE id = {ci.ProductId} FOR UPDATE")
                    .FirstOrDefaultAsync();

                if (p == null || !p.IsActive)
                {
                    await transaction.RollbackAsync();
                    return ServiceResult<OrderDto>.Fail(
                        $"Товар у кошику (ID {ci.ProductId}) приховано або знято з продажу. Оновіть кошик перед оформленням.",
                        409,
                        "PRODUCT_INACTIVE");
                }

                if (ci.Quantity <= 0)
                {
                    await transaction.RollbackAsync();
                    return ServiceResult<OrderDto>.Fail("Некоректна кількість товару в кошику.", 400);
                }

                if (p.StockQuantity < ci.Quantity)
                {
                    await transaction.RollbackAsync();
                    return ServiceResult<OrderDto>.Fail(
                        $"Недостатньо товару «{p.Name}» на складі: доступно {p.StockQuantity} шт., а в кошику {ci.Quantity} шт.",
                        409,
                        "INSUFFICIENT_STOCK");
                }

                lockedProducts[ci.ProductId] = p;
            }

            // Обчислюємо серверний відбиток актуального складу кошика та цін кожної позиції
            var actualSnapshotToken = CartService.ComputeCartSnapshotToken(
                cartItems.Select(ci => (ci.ProductId, ci.Quantity, lockedProducts[ci.ProductId].Price)));

            var serverTotalAmount = cartItems.Sum(ci => lockedProducts[ci.ProductId].Price * ci.Quantity);

            // Якщо склад кошика, кількість будь-якої позиції або ціна хоча б одного товару змінилися (навіть якщо загальна сума випадково збіглася) — повертаємо 409
            if (!string.Equals(clientConfirmedToken, actualSnapshotToken, StringComparison.OrdinalIgnoreCase) ||
                (dto.ExpectedTotalAmount.HasValue && Math.Round(dto.ExpectedTotalAmount.Value, 2) != Math.Round(serverTotalAmount, 2)))
            {
                await transaction.RollbackAsync();
                return ServiceResult<OrderDto>.Fail(
                    $"Склад кошика або ціни окремих товарів змінилися під час підготовки замовлення. Актуальна загальна сума становить {serverTotalAmount:0.00} грн. Оновіть кошик, перевірте позиції та підтвердьте оформлення ще раз.",
                    409,
                    "CART_OR_PRICE_CHANGED");
            }

            var newStatus = await _db.OrderStatuses.FirstOrDefaultAsync(s => s.Name == OrderStatus.New);
            if (newStatus == null)
            {
                await transaction.RollbackAsync();
                return ServiceResult<OrderDto>.Fail("Початковий статус «Нове» не налаштовано в системі.", 500);
            }

            var order = new Order
            {
                UserId = userId,
                OrderNumber = $"TMP-{Guid.NewGuid():N}"[..24],
                StatusId = newStatus.Id,
                RecipientFirstName = firstName,
                RecipientLastName = lastName,
                RecipientPhone = phone,
                RecipientEmail = email,
                Comment = comment,
                DeliveryMethod = "Самовивіз",
                PickupLocation = $"{_pickupSettings.Title} ({_pickupSettings.Address})",
                PaymentMethod = "Оплата при отриманні",
                IsPaid = false,
                PaidAt = null,
                IdempotencyKey = idempotencyKey,
                IdempotencyPayloadHash = requestPayloadHash,
                CreatedAt = DateTime.UtcNow
            };

            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            order.OrderNumber = $"PAPIR-{DateTime.UtcNow:yyyyMMdd}-{order.Id:D4}";

            foreach (var ci in cartItems)
            {
                var product = lockedProducts[ci.ProductId];

                // Зменшуємо залишок товару та інкрементуємо version, щоб будь-яка відкрита форма редагування в адмінці виявила зміну залишку
                var updatedRows = await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE products SET stock_quantity = stock_quantity - {ci.Quantity}, version = version + 1 WHERE id = {product.Id} AND stock_quantity >= {ci.Quantity}");

                if (updatedRows != 1)
                {
                    await transaction.RollbackAsync();
                    return ServiceResult<OrderDto>.Fail(
                        $"Недостатньо залишку для товару «{product.Name}» при конкурентному оформленні.",
                        409,
                        "INSUFFICIENT_STOCK");
                }

                _db.OrderItems.Add(new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = product.Id,
                    ProductNameSnapshot = product.Name,
                    PriceAtPurchase = product.Price,
                    Quantity = ci.Quantity
                });
            }

            // Очищуємо кошик користувача
            _db.CartItems.RemoveRange(cartItems);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            var createdOrder = await GetOrderQuery().FirstAsync(o => o.Id == order.Id);
            return ServiceResult<OrderDto>.Ok(MapOrder(createdOrder), 201);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return ServiceResult<OrderDto>.Fail("Не вдалося оформити замовлення через конфлікт збереження даних.", 409, "ORDER_SAVE_CONFLICT");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static string? ResolveClientConfirmationToken(CreateOrderRequestDto dto)
    {
        if (dto.ConfirmedItems != null && dto.ConfirmedItems.Count > 0)
        {
            var tokenFromItems = CartService.ComputeCartSnapshotToken(
                dto.ConfirmedItems.Select(i => (i.ProductId, i.Quantity, i.UnitPrice)));

            if (!string.IsNullOrWhiteSpace(dto.CartSnapshotToken) &&
                !string.Equals(dto.CartSnapshotToken.Trim(), tokenFromItems, StringComparison.OrdinalIgnoreCase))
            {
                return "__MISMATCHED_CLIENT_CONFIRMATION__";
            }

            return tokenFromItems;
        }

        if (!string.IsNullOrWhiteSpace(dto.CartSnapshotToken))
        {
            return dto.CartSnapshotToken.Trim();
        }

        return null;
    }

    private static string ComputeIdempotencyPayloadHash(
        string firstName,
        string lastName,
        string phone,
        string email,
        string? comment,
        string snapshotToken,
        decimal? expectedTotal)
    {
        var totalPart = expectedTotal.HasValue
            ? Math.Round(expectedTotal.Value, 2).ToString("0.00", CultureInfo.InvariantCulture)
            : "none";

        var raw = string.Join("|",
            firstName.ToLowerInvariant(),
            lastName.ToLowerInvariant(),
            phone.ToLowerInvariant(),
            email.ToLowerInvariant(),
            (comment ?? string.Empty).ToLowerInvariant(),
            snapshotToken.ToUpperInvariant(),
            totalPart);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    public async Task<List<OrderDto>> GetUserOrdersAsync(int userId)
    {
        var orders = await GetOrderQuery()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .ToListAsync();

        return orders.Select(MapOrder).ToList();
    }

    public async Task<ServiceResult<OrderDto>> GetUserOrderByIdAsync(int userId, int orderId)
    {
        var order = await GetOrderQuery().FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
        {
            return ServiceResult<OrderDto>.Fail("Замовлення не знайдено.", 404);
        }

        if (order.UserId != userId)
        {
            return ServiceResult<OrderDto>.Fail("Ви не маєте доступу до чужого замовлення.", 403, "FORBIDDEN");
        }

        return ServiceResult<OrderDto>.Ok(MapOrder(order));
    }

    public async Task<List<OrderDto>> GetAdminOrdersAsync(string? search, string? status)
    {
        var q = GetOrderQuery();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim();
            q = q.Where(o => o.Status != null && o.Status.Name == s);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(o =>
                o.OrderNumber.ToLower().Contains(term) ||
                o.RecipientFirstName.ToLower().Contains(term) ||
                o.RecipientLastName.ToLower().Contains(term) ||
                o.RecipientEmail.ToLower().Contains(term) ||
                o.RecipientPhone.ToLower().Contains(term) ||
                (o.User != null && (
                    o.User.FirstName.ToLower().Contains(term) ||
                    o.User.LastName.ToLower().Contains(term) ||
                    o.User.Email.ToLower().Contains(term)
                )));
        }

        var orders = await q
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .ToListAsync();

        return orders.Select(MapOrder).ToList();
    }

    public async Task<ServiceResult<OrderDto>> GetAdminOrderByIdAsync(int orderId)
    {
        var order = await GetOrderQuery().FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
        {
            return ServiceResult<OrderDto>.Fail("Замовлення не знайдено.", 404);
        }

        return ServiceResult<OrderDto>.Ok(MapOrder(order));
    }

    public async Task<ServiceResult<OrderDto>> UpdateOrderStatusAsync(int orderId, UpdateOrderStatusRequestDto dto)
    {
        var targetStatusName = dto.NewStatus?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetStatusName))
        {
            return ServiceResult<OrderDto>.Fail("Вкажіть цільовий статус замовлення.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        // Блокуємо рядок замовлення FOR UPDATE, щоб серіалізувати конкурентні запити оплати, зміни статусу та скасування
        var lockedOrder = await _db.Orders
            .FromSqlInterpolated($"SELECT * FROM orders WHERE id = {orderId} FOR UPDATE")
            .FirstOrDefaultAsync();

        if (lockedOrder == null)
        {
            await transaction.RollbackAsync();
            return ServiceResult<OrderDto>.Fail("Замовлення не знайдено.", 404);
        }

        var statuses = await _db.OrderStatuses.ToListAsync();
        var currentStatus = statuses.FirstOrDefault(s => s.Id == lockedOrder.StatusId);
        var targetStatus = statuses.FirstOrDefault(s => s.Name == targetStatusName);

        if (currentStatus == null || targetStatus == null)
        {
            await transaction.RollbackAsync();
            return ServiceResult<OrderDto>.Fail("Некоректний статус замовлення.", 400);
        }

        // Якщо спроба скасувати вже оплачене замовлення — забороняємо (повернення коштів у навчальному обсязі не передбачено)
        if (targetStatus.Name == OrderStatus.Cancelled && lockedOrder.IsPaid)
        {
            await transaction.RollbackAsync();
            return ServiceResult<OrderDto>.Fail(
                "Неможливо скасувати вже оплачене замовлення, оскільки повернення коштів після оплати при видачі в межах навчальної системи не проводиться. Завершіть видачу замовлення переведенням у статус «Виконане».",
                409,
                "CANNOT_CANCEL_PAID_ORDER");
        }

        var allowedNext = GetAllowedTransitionsForOrder(currentStatus.Name, lockedOrder.IsPaid);
        if (!allowedNext.Contains(targetStatus.Name))
        {
            await transaction.RollbackAsync();
            return ServiceResult<OrderDto>.Fail(
                $"Перехід зі статусу «{currentStatus.Name}» у «{targetStatus.Name}» заборонено правилами обробки.",
                409,
                "INVALID_STATUS_TRANSITION");
        }

        // Якщо перехід у «Виконане» — замовлення повинно бути оплаченим або оплата фіксується під час цієї операції видачі
        if (targetStatus.Name == OrderStatus.Completed)
        {
            if (!lockedOrder.IsPaid && !dto.ConfirmPaymentOnHandover)
            {
                await transaction.RollbackAsync();
                return ServiceResult<OrderDto>.Fail(
                    "Замовлення можна перевести у статус «Виконане» лише після фіксації оплати при видачі.",
                    409,
                    "PAYMENT_REQUIRED_FOR_COMPLETION");
            }

            if (!lockedOrder.IsPaid)
            {
                lockedOrder.IsPaid = true;
                lockedOrder.PaidAt = DateTime.UtcNow;
            }
        }

        // Якщо перехід у «Скасоване» — повертаємо залишки товарів рівно один раз у цій транзакції та інкрементуємо version товарів
        if (targetStatus.Name == OrderStatus.Cancelled)
        {
            var items = await _db.OrderItems
                .Where(oi => oi.OrderId == lockedOrder.Id)
                .OrderBy(oi => oi.ProductId)
                .ToListAsync();

            foreach (var item in items)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE products SET stock_quantity = stock_quantity + {item.Quantity}, version = version + 1 WHERE id = {item.ProductId}");
            }
        }

        lockedOrder.StatusId = targetStatus.Id;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var updatedOrder = await GetOrderQuery().FirstAsync(o => o.Id == orderId);
        return ServiceResult<OrderDto>.Ok(MapOrder(updatedOrder));
    }

    public async Task<ServiceResult<OrderDto>> MarkOrderPaidAsync(int orderId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();

        var lockedOrder = await _db.Orders
            .FromSqlInterpolated($"SELECT * FROM orders WHERE id = {orderId} FOR UPDATE")
            .FirstOrDefaultAsync();

        if (lockedOrder == null)
        {
            await transaction.RollbackAsync();
            return ServiceResult<OrderDto>.Fail("Замовлення не знайдено.", 404);
        }

        var currentStatus = await _db.OrderStatuses.FirstOrDefaultAsync(s => s.Id == lockedOrder.StatusId);
        if (currentStatus == null)
        {
            await transaction.RollbackAsync();
            return ServiceResult<OrderDto>.Fail("Некоректний статус замовлення.", 400);
        }

        if (currentStatus.Name == OrderStatus.Cancelled)
        {
            await transaction.RollbackAsync();
            return ServiceResult<OrderDto>.Fail("Неможливо зафіксувати оплату для скасованого замовлення.", 409, "ORDER_ALREADY_CANCELLED");
        }

        if (currentStatus.Name == OrderStatus.New)
        {
            await transaction.RollbackAsync();
            return ServiceResult<OrderDto>.Fail(
                "Не можна фіксувати оплату для нового замовлення до його прийняття в обробку. Спочатку переведіть замовлення у статус «В обробці».",
                409,
                "PAYMENT_NOT_ALLOWED_FOR_NEW_ORDER");
        }

        if (!lockedOrder.IsPaid)
        {
            lockedOrder.IsPaid = true;
            lockedOrder.PaidAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        var updatedOrder = await GetOrderQuery().FirstAsync(o => o.Id == orderId);
        return ServiceResult<OrderDto>.Ok(MapOrder(updatedOrder));
    }

    public static List<string> GetAllowedTransitionsForOrder(string statusName, bool isPaid)
    {
        if (!AllowedStatusTransitions.TryGetValue(statusName, out var baseTransitions))
        {
            return new List<string>();
        }

        var result = new List<string>(baseTransitions);

        // Якщо оплата вже зафіксована — скасування заборонено (повернення коштів не передбачено)
        if (isPaid)
        {
            result.Remove(OrderStatus.Cancelled);
        }

        return result;
    }

    private IQueryable<Order> GetOrderQuery()
    {
        return _db.Orders
            .AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.Status)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product);
    }

    public static OrderDto MapOrder(Order o)
    {
        var statusName = o.Status?.Name ?? OrderStatus.New;
        var transitions = GetAllowedTransitionsForOrder(statusName, o.IsPaid);

        var items = o.OrderItems
            .OrderBy(i => i.ProductId)
            .Select(i => new OrderItemDto
            {
                ProductId = i.ProductId,
                ProductName = !string.IsNullOrWhiteSpace(i.ProductNameSnapshot)
                    ? i.ProductNameSnapshot
                    : (i.Product?.Name ?? $"Товар #{i.ProductId}"),
                PriceAtPurchase = i.PriceAtPurchase,
                Quantity = i.Quantity
            })
            .ToList();

        var buyerName = o.User != null
            ? $"{o.User.FirstName} {o.User.LastName}".Trim()
            : $"{o.RecipientFirstName} {o.RecipientLastName}".Trim();

        return new OrderDto
        {
            Id = o.Id,
            OrderNumber = o.OrderNumber,
            UserId = o.UserId,
            BuyerFullName = buyerName,
            BuyerEmail = o.User?.Email ?? o.RecipientEmail,
            Status = statusName,
            AllowedTransitions = transitions,
            RecipientFirstName = o.RecipientFirstName,
            RecipientLastName = o.RecipientLastName,
            RecipientPhone = o.RecipientPhone,
            RecipientEmail = o.RecipientEmail,
            Comment = o.Comment,
            DeliveryMethod = o.DeliveryMethod,
            PickupLocation = o.PickupLocation,
            PaymentMethod = o.PaymentMethod,
            IsPaid = o.IsPaid,
            PaidAt = o.PaidAt.HasValue ? DateTime.SpecifyKind(o.PaidAt.Value, DateTimeKind.Utc) : null,
            CreatedAt = o.CreatedAt.HasValue ? DateTime.SpecifyKind(o.CreatedAt.Value, DateTimeKind.Utc) : DateTime.UtcNow,
            TotalQuantity = items.Sum(i => i.Quantity),
            TotalAmount = items.Sum(i => i.LineTotal),
            Items = items
        };
    }
}
