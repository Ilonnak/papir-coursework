using Microsoft.AspNetCore.Http;

namespace Backend.Services;

public class ImageStorageService
{
    private readonly IWebHostEnvironment _env;
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    private static readonly Dictionary<string, string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif"
    };

    public ImageStorageService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<ServiceResult<string>> SaveProductImageAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return ServiceResult<string>.Fail("Оберіть непорожній файл зображення.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return ServiceResult<string>.Fail("Розмір файлу перевищує максимально допустимі 5 МБ.");
        }

        if (!AllowedContentTypes.TryGetValue(file.ContentType, out var expectedExt))
        {
            return ServiceResult<string>.Fail("Дозволено лише растрові зображення у форматах JPEG, PNG, WEBP або GIF.");
        }

        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var readBytes = await stream.ReadAsync(header.AsMemory(0, header.Length));
        if (readBytes < 8 || !IsValidRasterSignature(header, readBytes, out var detectedExt))
        {
            return ServiceResult<string>.Fail("Файл не є коректним растровим зображенням (перевірка сигнатури файлу не пройдена).");
        }

        stream.Position = 0;

        var webRoot = _env.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
        }

        var uploadsDir = Path.Combine(webRoot, "uploads", "products");
        Directory.CreateDirectory(uploadsDir);

        // Генеруємо безпечне унікальне ім'я файлу без використання клієнтського імені (захист від path traversal та перезапису)
        var safeFileName = $"prod_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{detectedExt}";
        var fullPath = Path.GetFullPath(Path.Combine(uploadsDir, safeFileName));
        var normalizedUploadsDir = Path.GetFullPath(uploadsDir);

        if (!fullPath.StartsWith(normalizedUploadsDir, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<string>.Fail("Некоректний шлях збереження файлу.");
        }

        await using (var fileStream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await stream.CopyToAsync(fileStream);
        }

        var relativeUrl = $"/uploads/products/{safeFileName}";
        return ServiceResult<string>.Ok(relativeUrl, 201);
    }

    private static bool IsValidRasterSignature(byte[] header, int length, out string extension)
    {
        extension = string.Empty;

        // JPEG: FF D8 FF
        if (length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            extension = ".jpg";
            return true;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            extension = ".png";
            return true;
        }

        // GIF: GIF87a or GIF89a
        if (length >= 6 &&
            header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38 &&
            (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61)
        {
            extension = ".gif";
            return true;
        }

        // WEBP: RIFF....WEBP
        if (length >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            extension = ".webp";
            return true;
        }

        return false;
    }
}
