using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

public class CartItemDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
    public int AvailableStock { get; set; }
    public bool IsProductActive { get; set; }
    public bool HasIssue => !IsProductActive || AvailableStock <= 0 || Quantity > AvailableStock;
    public string? IssueDescription { get; set; }
}

public class CartResponseDto
{
    public List<CartItemDto> Items { get; set; } = new();
    public int TotalQuantity { get; set; }
    public decimal TotalAmount { get; set; }
    public bool CanCheckout { get; set; }
    public string CartSnapshotToken { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}

public class ConfirmedCartItemDto
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Range(1, 10000)]
    public int Quantity { get; set; }

    [Range(0.01, 99999999.99)]
    public decimal UnitPrice { get; set; }
}

public class AddToCartRequestDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Некоректний ідентифікатор товару.")]
    public int ProductId { get; set; }

    [Range(1, 10000, ErrorMessage = "Кількість повинна бути додатним цілим числом (від 1).")]
    public int Quantity { get; set; } = 1;
}

public class UpdateCartItemRequestDto
{
    [Range(1, 10000, ErrorMessage = "Кількість повинна бути додатним цілим числом (від 1).")]
    public int Quantity { get; set; }
}

public class PickupInfoDto
{
    public string Title { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Schedule { get; set; } = string.Empty;
    public string PaymentNote { get; set; } = string.Empty;
    public string DeliveryMethod => "Самовивіз";
    public string PaymentMethod => "Оплата при отриманні";
}

public class CreateOrderRequestDto
{
    [Required(ErrorMessage = "Вкажіть ім’я отримувача.")]
    [MaxLength(100, ErrorMessage = "Ім’я отримувача не може перевищувати 100 символів.")]
    public string RecipientFirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть прізвище отримувача.")]
    [MaxLength(100, ErrorMessage = "Прізвище отримувача не може перевищувати 100 символів.")]
    public string RecipientLastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть телефон отримувача.")]
    [MaxLength(30, ErrorMessage = "Телефон не може перевищувати 30 символів.")]
    [RegularExpression(@"^[\+\d\s\-\(\)]{7,25}$", ErrorMessage = "Вкажіть коректний номер телефону (наприклад, +380501234567).")]
    public string RecipientPhone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть email отримувача.")]
    [EmailAddress(ErrorMessage = "Вкажіть коректний email отримувача.")]
    [MaxLength(255, ErrorMessage = "Email не може перевищувати 255 символів.")]
    public string RecipientEmail { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Коментар не може перевищувати 500 символів.")]
    public string? Comment { get; set; }

    /// <summary>
    /// Серверний токен (відбиток) актуального складу кошика: охоплює ProductId, Quantity та UnitPrice кожної позиції.
    /// </summary>
    [MaxLength(200)]
    public string? CartSnapshotToken { get; set; }

    /// <summary>
    /// Явний перелік підтверджених позицій (ProductId, Quantity, UnitPrice) як альтернатива або доповнення до CartSnapshotToken.
    /// </summary>
    public List<ConfirmedCartItemDto>? ConfirmedItems { get; set; }

    /// <summary>
    /// Очікувана загальна сума (додаткова перевірка до підтвердження складу).
    /// </summary>
    public decimal? ExpectedTotalAmount { get; set; }

    /// <summary>
    /// Обов’язковий непорожній ключ ідемпотентності для захисту від дублювання при подвійному кліку чи повторі запиту.
    /// </summary>
    [Required(ErrorMessage = "Необхідно передати ключ ідемпотентності оформлення (IdempotencyKey).")]
    [MinLength(4, ErrorMessage = "Ключ ідемпотентності занадто короткий.")]
    [MaxLength(100, ErrorMessage = "Ключ ідемпотентності не може перевищувати 100 символів.")]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class OrderItemDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal PriceAtPurchase { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal => PriceAtPurchase * Quantity;
}

public class OrderDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string BuyerFullName { get; set; } = string.Empty;
    public string BuyerEmail { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<string> AllowedTransitions { get; set; } = new();
    public string RecipientFirstName { get; set; } = string.Empty;
    public string RecipientLastName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public string DeliveryMethod { get; set; } = string.Empty;
    public string PickupLocation { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalQuantity { get; set; }
    public decimal TotalAmount { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

public class UpdateOrderStatusRequestDto
{
    [Required(ErrorMessage = "Оберіть новий статус замовлення.")]
    [MaxLength(50)]
    public string NewStatus { get; set; } = string.Empty;

    /// <summary>
    /// Підтвердження отримання оплати при видачі (обов'язкове при переході у «Виконане», якщо замовлення ще не оплачене).
    /// </summary>
    public bool ConfirmPaymentOnHandover { get; set; }
}
