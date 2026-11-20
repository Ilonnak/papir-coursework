using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

public class CategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ProductCount { get; set; }
    public int ActiveProductCount { get; set; }
}

public class SaveCategoryRequestDto
{
    [Required(ErrorMessage = "Вкажіть назву категорії.")]
    [MaxLength(100, ErrorMessage = "Назва категорії не може перевищувати 100 символів.")]
    public string Name { get; set; } = string.Empty;
}

public class ProductDto
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Specifications { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}

public class CatalogQueryDto
{
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? Sort { get; set; } = "name";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 9;
}

public class PagedResultDto<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class SaveProductRequestDto
{
    [Required(ErrorMessage = "Оберіть категорію товару.")]
    [Range(1, int.MaxValue, ErrorMessage = "Оберіть коректну категорію.")]
    public int CategoryId { get; set; }

    [Required(ErrorMessage = "Вкажіть назву товару.")]
    [MaxLength(255, ErrorMessage = "Назва товару не може перевищувати 255 символів.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть опис товару.")]
    [MaxLength(4000, ErrorMessage = "Опис товару не може перевищувати 4000 символів.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть характеристики товару.")]
    [MaxLength(4000, ErrorMessage = "Характеристики не можуть перевищувати 4000 символів.")]
    public string Specifications { get; set; } = string.Empty;

    [Range(0.01, 99999999.99, ErrorMessage = "Ціна повинна бути додатною (від 0,01 до 99999999,99 грн).")]
    public decimal Price { get; set; }

    [Range(0, 1000000, ErrorMessage = "Залишок не може бути від’ємним (від 0 до 1 000 000 шт.).")]
    public int StockQuantity { get; set; }

    /// <summary>
    /// Очікувана версія запису товару (обов’язкова при редагуванні для захисту від застарілої форми).
    /// </summary>
    public int? Version { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(500, ErrorMessage = "Шлях до зображення не може перевищувати 500 символів.")]
    public string? ImagePath { get; set; }
}
