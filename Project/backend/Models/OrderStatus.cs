using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models;

[Table("order_statuses")]
public class OrderStatus
{
    public const string New = "Нове";
    public const string Processing = "В обробці";
    public const string Completed = "Виконане";
    public const string Cancelled = "Скасоване";

    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
