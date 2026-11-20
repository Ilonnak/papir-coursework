using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models;

[Table("cart_items")]
public class CartItem
{
    [Column("user_id")]
    public int UserId { get; set; }

    [Column("product_id")]
    public int ProductId { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public Product? Product { get; set; }
}
