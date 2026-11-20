using Backend.Data;
using Backend.DTOs;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class CatalogService
{
    private readonly AppDbContext _db;

    public CatalogService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<CategoryDto>> GetCategoriesAsync(bool includeInactiveCounts = false)
    {
        return await _db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                ProductCount = includeInactiveCounts ? c.Products.Count() : c.Products.Count(p => p.IsActive),
                ActiveProductCount = c.Products.Count(p => p.IsActive)
            })
            .ToListAsync();
    }

    public async Task<ServiceResult<CategoryDto>> CreateCategoryAsync(SaveCategoryRequestDto dto)
    {
        var name = dto.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult<CategoryDto>.Fail("Назва категорії не може бути порожньою.");
        }

        var exists = await _db.Categories.AnyAsync(c => c.Name.ToLower() == name.ToLower());
        if (exists)
        {
            return ServiceResult<CategoryDto>.Fail("Категорія з такою назвою вже існує.", 409);
        }

        var category = new Category { Name = name };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return ServiceResult<CategoryDto>.Ok(new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            ProductCount = 0,
            ActiveProductCount = 0
        }, 201);
    }

    public async Task<ServiceResult<CategoryDto>> UpdateCategoryAsync(int id, SaveCategoryRequestDto dto)
    {
        var category = await _db.Categories.Include(c => c.Products).FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
        {
            return ServiceResult<CategoryDto>.Fail("Категорію не знайдено.", 404);
        }

        var name = dto.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult<CategoryDto>.Fail("Назва категорії не може бути порожньою.");
        }

        var exists = await _db.Categories.AnyAsync(c => c.Id != id && c.Name.ToLower() == name.ToLower());
        if (exists)
        {
            return ServiceResult<CategoryDto>.Fail("Інша категорія з такою назвою вже існує.", 409);
        }

        category.Name = name;
        await _db.SaveChangesAsync();

        return ServiceResult<CategoryDto>.Ok(new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            ProductCount = category.Products.Count,
            ActiveProductCount = category.Products.Count(p => p.IsActive)
        });
    }

    public async Task<ServiceResult<bool>> DeleteCategoryAsync(int id)
    {
        var category = await _db.Categories.Include(c => c.Products).FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
        {
            return ServiceResult<bool>.Fail("Категорію не знайдено.", 404);
        }

        if (category.Products.Any())
        {
            return ServiceResult<bool>.Fail(
                $"Неможливо видалити категорію «{category.Name}», оскільки до неї прив’язано товарів: {category.Products.Count}. Спочатку перенесіть товари в іншу категорію.",
                409,
                "CATEGORY_NOT_EMPTY");
        }

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();
        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<PagedResultDto<ProductDto>>> GetPublicCatalogAsync(CatalogQueryDto query)
    {
        if (query.MinPrice.HasValue && query.MinPrice.Value < 0)
        {
            return ServiceResult<PagedResultDto<ProductDto>>.Fail("Мінімальна ціна не може бути від’ємною.");
        }

        if (query.MaxPrice.HasValue && query.MaxPrice.Value < 0)
        {
            return ServiceResult<PagedResultDto<ProductDto>>.Fail("Максимальна ціна не може бути від’ємною.");
        }

        if (query.MinPrice.HasValue && query.MaxPrice.HasValue && query.MinPrice.Value > query.MaxPrice.Value)
        {
            return ServiceResult<PagedResultDto<ProductDto>>.Fail("Мінімальна ціна («Від») не може перевищувати максимальну («До»).");
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 9 : query.PageSize, 1, 48);

        var q = _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive);

        if (query.CategoryId.HasValue && query.CategoryId.Value > 0)
        {
            q = q.Where(p => p.CategoryId == query.CategoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            q = q.Where(p => p.Name.ToLower().Contains(term));
        }

        if (query.MinPrice.HasValue)
        {
            q = q.Where(p => p.Price >= query.MinPrice.Value);
        }

        if (query.MaxPrice.HasValue)
        {
            q = q.Where(p => p.Price <= query.MaxPrice.Value);
        }

        var totalCount = await q.CountAsync();

        q = (query.Sort?.ToLowerInvariant()) switch
        {
            "price_asc" or "cheap" => q.OrderBy(p => p.Price).ThenBy(p => p.Name),
            "price_desc" or "expensive" => q.OrderByDescending(p => p.Price).ThenBy(p => p.Name),
            "name_desc" => q.OrderByDescending(p => p.Name),
            _ => q.OrderBy(p => p.Name)
        };

        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
        if (page > totalPages)
        {
            page = totalPages;
        }

        var items = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => MapProductExpr(p))
            .ToListAsync();

        return ServiceResult<PagedResultDto<ProductDto>>.Ok(new PagedResultDto<ProductDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages
        });
    }

    public async Task<ServiceResult<ProductDto>> GetPublicProductByIdAsync(int id)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive);

        if (product == null)
        {
            return ServiceResult<ProductDto>.Fail("Товар не знайдено або він тимчасово недоступний у каталозі.", 404, "PRODUCT_NOT_FOUND");
        }

        return ServiceResult<ProductDto>.Ok(MapProduct(product));
    }

    public async Task<List<ProductDto>> GetAdminProductsAsync(string? search, string? visibility, int? categoryId)
    {
        var q = _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(p => p.Name.ToLower().Contains(term));
        }

        if (categoryId.HasValue && categoryId.Value > 0)
        {
            q = q.Where(p => p.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(visibility))
        {
            q = visibility.ToLowerInvariant() switch
            {
                "active" => q.Where(p => p.IsActive),
                "hidden" => q.Where(p => !p.IsActive),
                _ => q
            };
        }

        var list = await q
            .OrderBy(p => p.Id)
            .ToListAsync();

        return list.Select(MapProduct).ToList();
    }

    public async Task<ServiceResult<ProductDto>> CreateProductAsync(SaveProductRequestDto dto)
    {
        var validationError = ValidateProductDto(dto);
        if (validationError != null)
        {
            return ServiceResult<ProductDto>.Fail(validationError);
        }

        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == dto.CategoryId);
        if (category == null)
        {
            return ServiceResult<ProductDto>.Fail("Обрану категорію не знайдено.", 400);
        }

        var imagePath = NormalizeImagePath(dto.ImagePath);

        var product = new Product
        {
            CategoryId = dto.CategoryId,
            Name = dto.Name.Trim(),
            Description = dto.Description.Trim(),
            Specifications = dto.Specifications.Trim(),
            Price = Math.Round(dto.Price, 2),
            StockQuantity = dto.StockQuantity,
            Version = 1,
            IsActive = dto.IsActive,
            ImagePath = imagePath,
            CreatedAt = DateTime.UtcNow
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        product.Category = category;
        return ServiceResult<ProductDto>.Ok(MapProduct(product), 201);
    }

    public async Task<ServiceResult<ProductDto>> UpdateProductAsync(int id, SaveProductRequestDto dto)
    {
        var validationError = ValidateProductDto(dto);
        if (validationError != null)
        {
            return ServiceResult<ProductDto>.Fail(validationError);
        }

        if (!dto.Version.HasValue || dto.Version.Value < 1)
        {
            return ServiceResult<ProductDto>.Fail(
                "Для збереження змін товару необхідно передати актуальну версію запису (Version). Оновіть форму та спробуйте ще раз.",
                400,
                "VERSION_REQUIRED");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        var product = await _db.Products
            .FromSqlInterpolated($"SELECT * FROM products WHERE id = {id} FOR UPDATE")
            .FirstOrDefaultAsync();

        if (product == null)
        {
            await transaction.RollbackAsync();
            return ServiceResult<ProductDto>.Fail("Товар не знайдено.", 404);
        }

        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == dto.CategoryId);
        if (category == null)
        {
            await transaction.RollbackAsync();
            return ServiceResult<ProductDto>.Fail("Обрану категорію не знайдено.", 400);
        }

        // Перевіряємо, чи не змінився товар (зокрема залишок при покупці чи скасуванні або іншим адміністратором) з моменту відкриття форми
        if (product.Version != dto.Version.Value)
        {
            await transaction.RollbackAsync();
            var currentCategory = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == product.CategoryId);
            product.Category = currentCategory;
            var freshDto = MapProduct(product);

            return ServiceResult<ProductDto>.Fail(
                $"Дані товару «{product.Name}» змінилися (наприклад, змінився залишок на складі до {product.StockQuantity} шт. через оформлення або скасування замовлення). Форму оновлено актуальними даними (версія #{product.Version}). Перевірте залишок і повторно підтвердьте збереження.",
                409,
                "PRODUCT_CONCURRENCY_CONFLICT",
                freshDto);
        }

        product.CategoryId = dto.CategoryId;
        product.Name = dto.Name.Trim();
        product.Description = dto.Description.Trim();
        product.Specifications = dto.Specifications.Trim();
        product.Price = Math.Round(dto.Price, 2);
        product.StockQuantity = dto.StockQuantity;
        product.IsActive = dto.IsActive;
        product.ImagePath = NormalizeImagePath(dto.ImagePath);
        product.Version += 1;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        product.Category = category;
        return ServiceResult<ProductDto>.Ok(MapProduct(product));
    }

    public async Task<ServiceResult<ProductDto>> ToggleVisibilityAsync(int id, bool isActive)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();

        var product = await _db.Products
            .FromSqlInterpolated($"SELECT * FROM products WHERE id = {id} FOR UPDATE")
            .FirstOrDefaultAsync();

        if (product == null)
        {
            await transaction.RollbackAsync();
            return ServiceResult<ProductDto>.Fail("Товар не знайдено.", 404);
        }

        product.IsActive = isActive;
        product.Version += 1;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var category = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == product.CategoryId);
        product.Category = category;
        return ServiceResult<ProductDto>.Ok(MapProduct(product));
    }

    private static string? ValidateProductDto(SaveProductRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return "Назва товару є обов’язковою.";
        if (dto.Name.Trim().Length > 255)
            return "Назва товару не може перевищувати 255 символів.";
        if (string.IsNullOrWhiteSpace(dto.Description))
            return "Опис товару є обов’язковим.";
        if (string.IsNullOrWhiteSpace(dto.Specifications))
            return "Характеристики товару є обов’язковими.";
        if (dto.Price <= 0)
            return "Ціна товару повинна бути більшою за 0.";
        if (dto.StockQuantity < 0)
            return "Залишок на складі не може бути від’ємним.";
        return null;
    }

    public static string NormalizeImagePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return "/images/products/placeholder.svg";
        }

        var trimmed = rawPath.Trim().Replace('\\', '/');
        if (trimmed.Contains(".."))
        {
            return "/images/products/placeholder.svg";
        }

        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }

        return trimmed;
    }

    private static ProductDto MapProductExpr(Product p) => new()
    {
        Id = p.Id,
        CategoryId = p.CategoryId,
        CategoryName = p.Category != null ? p.Category.Name : string.Empty,
        Name = p.Name,
        Description = p.Description,
        Specifications = p.Specifications,
        Price = p.Price,
        StockQuantity = p.StockQuantity,
        Version = p.Version,
        IsActive = p.IsActive,
        ImagePath = string.IsNullOrWhiteSpace(p.ImagePath) ? "/images/products/placeholder.svg" : p.ImagePath,
        CreatedAt = p.CreatedAt.HasValue ? DateTime.SpecifyKind(p.CreatedAt.Value, DateTimeKind.Utc) : null
    };

    public static ProductDto MapProduct(Product p) => MapProductExpr(p);
}
