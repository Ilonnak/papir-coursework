using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models;

[Table("order_items")]
public class OrderItem
{
    [Column("order_id")]
    public int OrderId { get; set; }

    [Column("product_id")]
    public int ProductId { get; set; }

    [Required]
    [MaxLength(255)]
    [Column("product_name_snapshot")]
    public string ProductNameSnapshot { get; set; } = string.Empty;

    [Column("price_at_purchase", TypeName = "decimal(10,2)")]
    public decimal PriceAtPurchase { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; }

    public Order? Order { get; set; }
    public Product? Product { get; set; }
}
