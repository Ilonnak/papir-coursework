using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models;

[Table("orders")]
public class Order
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("order_number")]
    public string OrderNumber { get; set; } = string.Empty;

    [Column("status_id")]
    public int StatusId { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("recipient_first_name")]
    public string RecipientFirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Column("recipient_last_name")]
    public string RecipientLastName { get; set; } = string.Empty;

    [Required]
    [MaxLength(30)]
    [Column("recipient_phone")]
    public string RecipientPhone { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    [Column("recipient_email")]
    public string RecipientEmail { get; set; } = string.Empty;

    [MaxLength(500)]
    [Column("comment")]
    public string? Comment { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("delivery_method")]
    public string DeliveryMethod { get; set; } = "Самовивіз";

    [Required]
    [MaxLength(255)]
    [Column("pickup_location")]
    public string PickupLocation { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    [Column("payment_method")]
    public string PaymentMethod { get; set; } = "Оплата при отриманні";

    [Column("is_paid")]
    public bool IsPaid { get; set; }

    [Column("paid_at")]
    public DateTime? PaidAt { get; set; }

    [MaxLength(100)]
    [Column("idempotency_key")]
    public string? IdempotencyKey { get; set; }

    [MaxLength(128)]
    [Column("idempotency_payload_hash")]
    public string? IdempotencyPayloadHash { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public OrderStatus? Status { get; set; }
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
