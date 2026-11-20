using System.Text;
using Backend.Configuration;
using Backend.Data;
using Backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Services;

public class DatabaseInitializer
{
    private readonly AppDbContext _db;
    private readonly DemoSeedSettings _demoSettings;
    private readonly PickupPointSettings _pickupSettings;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public DatabaseInitializer(
        AppDbContext db,
        IOptions<DemoSeedSettings> demoOptions,
        IOptions<PickupPointSettings> pickupOptions,
        IWebHostEnvironment env,
        ILogger<DatabaseInitializer> logger)
    {
        _db = db;
        _demoSettings = demoOptions.Value;
        _pickupSettings = pickupOptions.Value;
        _env = env;
        _logger = logger;
    }

    public async Task InitializeSchemaAndStatusesAsync()
    {
        // Гарантуємо наявність базової схеми (для нової порожньої чи тестової БД) та розширень 001 без DROP / TRUNCATE
        var baseSqlPath = Path.Combine(_env.ContentRootPath, "Database", "000_full_schema_for_empty_db.sql");
        if (File.Exists(baseSqlPath))
        {
            var baseSql = await File.ReadAllTextAsync(baseSqlPath);
            await _db.Database.ExecuteSqlRawAsync(baseSql);
        }

        var extendSqlPath = Path.Combine(_env.ContentRootPath, "Database", "001_extend_existing_schema.sql");
        if (File.Exists(extendSqlPath))
        {
            var extendSql = await File.ReadAllTextAsync(extendSqlPath);
            await _db.Database.ExecuteSqlRawAsync(extendSql);
        }

        var extend002SqlPath = Path.Combine(_env.ContentRootPath, "Database", "002_concurrency_and_idempotency.sql");
        if (File.Exists(extend002SqlPath))
        {
            var extend002Sql = await File.ReadAllTextAsync(extend002SqlPath);
            await _db.Database.ExecuteSqlRawAsync(extend002Sql);
        }

        // Гарантуємо 4 обов'язкові статуси замовлень
        var requiredStatuses = new[] { OrderStatus.New, OrderStatus.Processing, OrderStatus.Completed, OrderStatus.Cancelled };
        var existingStatuses = await _db.OrderStatuses.Select(s => s.Name).ToListAsync();
        foreach (var statusName in requiredStatuses)
        {
            if (!existingStatuses.Contains(statusName))
            {
                _db.OrderStatuses.Add(new OrderStatus { Name = statusName });
            }
        }
        await _db.SaveChangesAsync();

        EnsurePlaceholderSvgExists();
    }

    public async Task SeedDemoDataIfDevelopmentAsync(bool forceSeed = false)
    {
        if (!_env.IsDevelopment() && !forceSeed)
        {
            return;
        }

        if (!_demoSettings.Enabled && !forceSeed)
        {
            return;
        }

        EnsureDemoProductImagesExist();

        // 1. Адміністратор (без перезапису існуючого)
        if (!string.IsNullOrWhiteSpace(_demoSettings.AdminAccount.Email))
        {
            await EnsureUserAsync(_demoSettings.AdminAccount, isAdmin: true);
        }

        // 2. Покупці (два акаунти для перевірки ізоляції даних)
        foreach (var buyerCfg in _demoSettings.CustomerAccounts)
        {
            if (!string.IsNullOrWhiteSpace(buyerCfg.Email))
            {
                await EnsureUserAsync(buyerCfg, isAdmin: false);
            }
        }

        // 3. Категорії (щонайменше 5 категорій)
        var categoryNames = new[]
        {
            "Зошити та блокноти",
            "Письмове приладдя",
            "Папки та архівація",
            "Органайзери та настільне приладдя",
            "Папір та стікери",
            "Креслення та творчість"
        };

        var categoryMap = new Dictionary<string, Category>();
        foreach (var catName in categoryNames)
        {
            var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Name.ToLower() == catName.ToLower());
            if (cat == null)
            {
                cat = new Category { Name = catName };
                _db.Categories.Add(cat);
                await _db.SaveChangesAsync();
            }
            categoryMap[catName] = cat;
        }

        // 4. Товари (23 демонстраційні товари, включаючи товар без залишку та прихований товар; «Тестовий блокнот А6» не входить до стандартного демонстраційного наповнення)
        // Зіставлення виконується за незмінним ідентифікатором демонстраційного товару (SeedKey), який не залежить від подальшого ручного перейменування товару чи числового ID.
        // Для вже існуючих записів без заповненого SeedKey виконується одноразова прив'язка SeedKey без зміни назви, ціни, опису, залишку чи фото.
        var demoProducts = GetDemoProductsCatalog();
        foreach (var dp in demoProducts)
        {
            var existingBySeedKey = await _db.Products.FirstOrDefaultAsync(p => p.SeedKey == dp.SeedKey);
            if (existingBySeedKey != null)
            {
                // Демонстраційний товар уже присутній у базі (навіть якщо його перейменували чи змінили фото/ціну) — нічого не перезаписуємо
                continue;
            }

            var normalizedName = dp.Name.Trim().ToLower();
            var existingLegacy = await _db.Products.FirstOrDefaultAsync(
                p => p.SeedKey == null && (p.Name.ToLower() == normalizedName || p.ImagePath == dp.ImagePath));

            if (existingLegacy != null)
            {
                // Одноразово проставляємо незмінний SeedKey існуючому демонстраційному товару, не змінюючи жодних користувацьких полів
                existingLegacy.SeedKey = dp.SeedKey;
                continue;
            }

            if (categoryMap.TryGetValue(dp.CategoryName, out var cat))
            {
                _db.Products.Add(new Product
                {
                    CategoryId = cat.Id,
                    SeedKey = dp.SeedKey,
                    Name = dp.Name,
                    Description = dp.Description,
                    Specifications = dp.Specifications,
                    Price = dp.Price,
                    StockQuantity = dp.StockQuantity,
                    IsActive = dp.IsActive,
                    ImagePath = dp.ImagePath,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
        await _db.SaveChangesAsync();

        // 5. Демонстраційні замовлення для двох покупців (лише якщо в таблиці orders ще немає жодного запису)
        if (!await _db.Orders.AnyAsync())
        {
            await SeedInitialDemoOrdersAsync();
        }

        _logger.LogInformation("Демонстраційні дані каталогу «папір.» успішно перевірено та підготовлено.");
    }

    private async Task EnsureUserAsync(DemoAccountConfig cfg, bool isAdmin)
    {
        var email = cfg.Email.Trim().ToLowerInvariant();
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
        if (existing != null)
        {
            return;
        }

        var user = new User
        {
            Email = email,
            FirstName = cfg.FirstName.Trim(),
            LastName = cfg.LastName.Trim(),
            MiddleName = string.IsNullOrWhiteSpace(cfg.MiddleName) ? null : cfg.MiddleName.Trim(),
            IsAdmin = isAdmin,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, cfg.Password);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
    }

    private async Task SeedInitialDemoOrdersAsync()
    {
        var olena = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == "olena@papir.local");
        var andrii = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == "andrii@papir.local");
        if (olena == null || andrii == null) return;

        var statuses = await _db.OrderStatuses.ToDictionaryAsync(s => s.Name, s => s.Id);
        var products = await _db.Products.ToListAsync();
        if (products.Count < 6) return;

        var notebookOlive = products.First(p => p.SeedKey == "demo-notebook-olive-a5" || p.Name.Contains("Блокнот А5, 80 аркушів, оливковий"));
        var folderSand = products.First(p => p.SeedKey == "demo-folder-sand-a4" || p.Name.Contains("Папка для документів А4, пісочна"));
        var notebookPowder = products.First(p => p.SeedKey == "demo-notebook-powder-a5" || p.Name.Contains("Блокнот А5, 96 аркушів, пудровий"));
        var pensSet = products.First(p => p.SeedKey == "demo-pens-set-3" || p.Name.Contains("Набір кулькових ручок, 3 шт."));
        var notebookTerracotta = products.First(p => p.SeedKey == "demo-notebook-terracotta-a5" || p.Name.Contains("Зошит А5, 48 аркушів, теракотовий"));

        var pickupLoc = $"{_pickupSettings.Title} ({_pickupSettings.Address})";

        var order1 = new Order
        {
            UserId = olena.Id,
            OrderNumber = "PAPIR-DEMO-0001",
            StatusId = statuses[OrderStatus.New],
            RecipientFirstName = olena.FirstName,
            RecipientLastName = olena.LastName,
            RecipientPhone = "+380501112233",
            RecipientEmail = olena.Email,
            Comment = "Заберу після пар у навчальному пункті.",
            DeliveryMethod = "Самовивіз",
            PickupLocation = pickupLoc,
            PaymentMethod = "Оплата при отриманні",
            IsPaid = false,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };

        var order2 = new Order
        {
            UserId = olena.Id,
            OrderNumber = "PAPIR-DEMO-0002",
            StatusId = statuses[OrderStatus.Processing],
            RecipientFirstName = olena.FirstName,
            RecipientLastName = olena.LastName,
            RecipientPhone = "+380501112233",
            RecipientEmail = olena.Email,
            Comment = "Прохання перевірити цілісність обкладинки.",
            DeliveryMethod = "Самовивіз",
            PickupLocation = pickupLoc,
            PaymentMethod = "Оплата при отриманні",
            IsPaid = false,
            CreatedAt = DateTime.UtcNow.AddDays(-4)
        };

        var order3 = new Order
        {
            UserId = olena.Id,
            OrderNumber = "PAPIR-DEMO-0003",
            StatusId = statuses[OrderStatus.Completed],
            RecipientFirstName = olena.FirstName,
            RecipientLastName = olena.LastName,
            RecipientPhone = "+380501112233",
            RecipientEmail = olena.Email,
            DeliveryMethod = "Самовивіз",
            PickupLocation = pickupLoc,
            PaymentMethod = "Оплата при отриманні",
            IsPaid = true,
            PaidAt = DateTime.UtcNow.AddDays(-6),
            CreatedAt = DateTime.UtcNow.AddDays(-7)
        };

        var order4 = new Order
        {
            UserId = andrii.Id,
            OrderNumber = "PAPIR-DEMO-0004",
            StatusId = statuses[OrderStatus.Cancelled],
            RecipientFirstName = andrii.FirstName,
            RecipientLastName = andrii.LastName,
            RecipientPhone = "+380672223344",
            RecipientEmail = andrii.Email,
            Comment = "Помилково обрав не ту кількість.",
            DeliveryMethod = "Самовивіз",
            PickupLocation = pickupLoc,
            PaymentMethod = "Оплата при отриманні",
            IsPaid = false,
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        };

        _db.Orders.AddRange(order1, order2, order3, order4);
        await _db.SaveChangesAsync();

        _db.OrderItems.AddRange(
            new OrderItem
            {
                OrderId = order1.Id,
                ProductId = notebookOlive.Id,
                ProductNameSnapshot = notebookOlive.Name,
                PriceAtPurchase = notebookOlive.Price,
                Quantity = 2
            },
            new OrderItem
            {
                OrderId = order1.Id,
                ProductId = folderSand.Id,
                ProductNameSnapshot = folderSand.Name,
                PriceAtPurchase = folderSand.Price,
                Quantity = 1
            },
            new OrderItem
            {
                OrderId = order2.Id,
                ProductId = notebookPowder.Id,
                ProductNameSnapshot = notebookPowder.Name,
                PriceAtPurchase = notebookPowder.Price,
                Quantity = 1
            },
            new OrderItem
            {
                OrderId = order2.Id,
                ProductId = pensSet.Id,
                ProductNameSnapshot = pensSet.Name,
                PriceAtPurchase = pensSet.Price,
                Quantity = 1
            },
            new OrderItem
            {
                OrderId = order3.Id,
                ProductId = notebookTerracotta.Id,
                ProductNameSnapshot = notebookTerracotta.Name,
                PriceAtPurchase = notebookTerracotta.Price,
                Quantity = 4
            },
            new OrderItem
            {
                OrderId = order4.Id,
                ProductId = pensSet.Id,
                ProductNameSnapshot = pensSet.Name,
                PriceAtPurchase = pensSet.Price,
                Quantity = 2
            }
        );

        await _db.SaveChangesAsync();
    }

    private void EnsurePlaceholderSvgExists()
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var productsImgDir = Path.Combine(webRoot, "images", "products");
        Directory.CreateDirectory(productsImgDir);

        var placeholderPath = Path.Combine(productsImgDir, "placeholder.svg");
        if (!File.Exists(placeholderPath))
        {
            File.WriteAllText(placeholderPath, BuildProductSvg("папір.", "Канцелярський товар", "#6D2038", "#F1E5E8", "notebook"), Encoding.UTF8);
        }
    }

    private void EnsureDemoProductImagesExist()
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var productsImgDir = Path.Combine(webRoot, "images", "products");
        var uploadsImgDir = Path.Combine(webRoot, "uploads", "products");
        Directory.CreateDirectory(productsImgDir);
        Directory.CreateDirectory(uploadsImgDir);

        foreach (var item in GetDemoProductsCatalog())
        {
            if (string.IsNullOrWhiteSpace(item.ImagePath)) continue;
            var relPath = item.ImagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(webRoot, relPath);
            if (!File.Exists(fullPath) && item.ImagePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                var svg = BuildProductSvg(item.ShortLabel, item.CategoryName, item.AccentColor, item.BgColor, item.IconKind);
                File.WriteAllText(fullPath, svg, Encoding.UTF8);
            }
        }
    }

    private static string BuildProductSvg(string title, string subtitle, string accentHex, string bgHex, string iconKind)
    {
        var illustration = iconKind switch
        {
            "pen" => $@"
                <g transform='translate(200,145) rotate(24)'>
                  <rect x='-36' y='-70' width='14' height='135' rx='6' fill='{accentHex}' />
                  <polygon points='-36,65 -22,65 -29,82' fill='#292326' />
                  <rect x='-6' y='-65' width='14' height='130' rx='6' fill='#8C5868' />
                  <polygon points='-6,65 8,65 1,82' fill='#292326' />
                  <rect x='24' y='-60' width='14' height='125' rx='6' fill='#B78392' />
                  <polygon points='24,65 38,65 31,82' fill='#292326' />
                </g>",
            "folder" => $@"
                <g transform='translate(200,145) rotate(-5)'>
                  <path d='M-75,-50 L-20,-50 L-8,-36 L75,-36 C80,-36 84,-32 84,-27 L84,52 C84,57 80,61 75,61 L-75,61 C-80,61 -84,57 -84,52 L-84,-41 C-84,-46 -80,-50 -75,-50 Z' fill='{accentHex}' />
                  <rect x='-68' y='-24' width='136' height='74' rx='4' fill='#FAF7F3' opacity='0.22' />
                  <rect x='42' y='-36' width='6' height='97' fill='#292326' opacity='0.28' />
                </g>",
            "organizer" => $@"
                <g transform='translate(200,150)'>
                  <rect x='-65' y='-20' width='130' height='75' rx='10' fill='{accentHex}' />
                  <rect x='-50' y='-65' width='26' height='50' rx='4' fill='#B57C8C' />
                  <rect x='-12' y='-75' width='16' height='60' rx='5' fill='#51172A' />
                  <rect x='18' y='-55' width='30' height='40' rx='4' fill='#D1A7B3' />
                  <line x1='-45' y1='18' x2='45' y2='18' stroke='#FAF7F3' stroke-width='3' opacity='0.6' />
                </g>",
            "paper" => $@"
                <g transform='translate(200,145)'>
                  <rect x='-52' y='-58' width='108' height='126' rx='6' fill='#DED4D7' transform='translate(8,8)' />
                  <rect x='-52' y='-58' width='108' height='126' rx='6' fill='#FFFFFF' stroke='{accentHex}' stroke-width='3' />
                  <line x1='-32' y1='-28' x2='34' y2='-28' stroke='{accentHex}' stroke-width='3' opacity='0.7' />
                  <line x1='-32' y1='-8' x2='34' y2='-8' stroke='{accentHex}' stroke-width='2' opacity='0.45' />
                  <line x1='-32' y1='12' x2='20' y2='12' stroke='{accentHex}' stroke-width='2' opacity='0.45' />
                  <rect x='10' y='24' width='34' height='30' rx='3' fill='{accentHex}' opacity='0.85' />
                </g>",
            "art" => $@"
                <g transform='translate(200,145)'>
                  <polygon points='-65,55 65,55 65,-45' fill='none' stroke='{accentHex}' stroke-width='8' stroke-linejoin='round' />
                  <line x1='-50' y1='-55' x2='45' y2='40' stroke='#51172A' stroke-width='6' stroke-linecap='round' />
                  <circle cx='-25' cy='-15' r='18' fill='{accentHex}' opacity='0.25' />
                </g>",
            _ => $@"
                <g transform='translate(200,145) rotate(-7)'>
                  <rect x='-52' y='-68' width='104' height='138' rx='8' fill='#D9CCD0' transform='translate(7,7)' />
                  <rect x='-52' y='-68' width='104' height='138' rx='4 10 10 4' fill='{accentHex}' />
                  <rect x='-52' y='-68' width='14' height='138' rx='4 0 0 4' fill='#292326' opacity='0.28' />
                  <rect x='-24' y='-32' width='58' height='26' rx='4' fill='#FAF7F3' opacity='0.92' />
                </g>"
        };

        var safeTitle = System.Security.SecurityElement.Escape(title);
        var safeSub = System.Security.SecurityElement.Escape(subtitle);

        return $@"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 400 320' width='400' height='320'>
  <rect width='400' height='320' rx='16' fill='{bgHex}' />
  <circle cx='200' cy='140' r='96' fill='#FFFFFF' opacity='0.65' />
  {illustration}
  <text x='200' y='272' text-anchor='middle' font-family='Segoe UI, Arial, sans-serif' font-size='15' font-weight='700' fill='#292326'>{safeTitle}</text>
  <text x='200' y='294' text-anchor='middle' font-family='Segoe UI, Arial, sans-serif' font-size='11' fill='#6D2038' letter-spacing='1'>{safeSub.ToUpperInvariant()}</text>
</svg>";
    }

    private record DemoProductSeed(
        string SeedKey,
        string Name,
        string CategoryName,
        decimal Price,
        int StockQuantity,
        bool IsActive,
        string ImagePath,
        string Description,
        string Specifications,
        string ShortLabel,
        string AccentColor,
        string BgColor,
        string IconKind);

    private static List<DemoProductSeed> GetDemoProductsCatalog() => new()
    {
        new(
            "demo-notebook-olive-a5",
            "Блокнот А5, 80 аркушів, оливковий",
            "Зошити та блокноти",
            125.00m,
            24,
            true,
            "/uploads/products/prod_20261108171307_787b367f9b0b4cebb3e34e71d2d20e78.png",
            "Компактний блокнот для щоденних записів, конспектів, навчання та особистого планування. Тверда матова обкладинка надійно захищає сторінки у рюкзаку чи сумці.",
            "Формат: А5 (148×210 мм)\nКількість аркушів: 80\nЛінування: Клітинка (5×5 мм)\nЩільність паперу: 80 г/м², кремовий блок\nОбкладинка: Тверда текстурована\nКолір: Оливковий",
            "Блокнот А5 · Оливковий",
            "#6D7B58",
            "#F3F4EE",
            "notebook"
        ),
        new(
            "demo-notebook-powder-a5",
            "Блокнот А5, 96 аркушів, пудровий",
            "Зошити та блокноти",
            145.00m,
            18,
            true,
            "/uploads/products/prod_20261108171201_99a69dae06744894aa090d287f06c98d.png",
            "Стриманий записник на 96 аркушів у крапку (dot grid) із закладкою-лясе та еластичною гумкою-фіксатором. Ідеальний для ведення bullet journal та робочих нотаток.",
            "Формат: А5 (148×210 мм)\nКількість аркушів: 96\nЛінування: Крапка (5 мм)\nЩільність паперу: 90 г/м²\nОбкладинка: Тверда під льон\nКолір: Пудрово-рожевий",
            "Блокнот А5 · Пудровий",
            "#A86476",
            "#F8EFF2",
            "notebook"
        ),
        new(
            "demo-notebook-terracotta-a5",
            "Зошит А5, 48 аркушів, теракотовий",
            "Зошити та блокноти",
            38.00m,
            45,
            true,
            "/uploads/products/prod_20261108171102_28bcd4811515473e84f0e30a97f30189.png",
            "Навчальний зошит із щільною картонної обкладинкою софт-тач та чіткою ліновкою. Легкий та практичний для лекцій і семінарів.",
            "Формат: А5\nКількість аркушів: 48\nЛінування: Лінія\nКріплення: Металева скоба\nЩільність паперу: 70 г/м²\nКолір: Теракотовий",
            "Зошит А5 · 48 арк.",
            "#9E5243",
            "#F9F0ED",
            "notebook"
        ),
        new(
            "demo-notebook-burgundy-a4",
            "Блокнот А4 на спіралі, 120 аркушів, бургунді",
            "Зошити та блокноти",
            195.00m,
            14,
            true,
            "/uploads/products/prod_20261108170940_282fe60ecc404f1bbd86f9a4b7c39df6.png",
            "Великий коледж-блок формату А4 із бічною металевою спіраллю та мікроперфорацією для акуратного відривання аркушів. Зручний для конспектування великих курсів.",
            "Формат: А4 (210×297 мм)\nКількість аркушів: 120\nЛінування: Клітинка з рамкою\nКріплення: Подвійна євроспіраль\nЩільність паперу: 80 г/м²\nКолір: Бургунді",
            "Коледж-блок А4 · Бургунді",
            "#6D2038",
            "#F5EAEE",
            "notebook"
        ),
        new(
            "demo-planner-graphite-a5",
            "Планер тижневий недатований А5, графітовий",
            "Зошити та блокноти",
            390.00m,
            10,
            true,
            "/uploads/products/prod_20261108170830_9b1261f966d743db9a36861fd8d106ac.png",
            "Недатований тижневий планер на рік зі сторінками для цілей, трекером звичок та фінансовим плануванням. Можна розпочати з будь-якого тижня.",
            "Формат: А5\nКількість сторінок: 160\nПапір: Шведський кремовий 100 г/м²\nОбкладинка: Тверда екошкіра\nКолір: Графітовий",
            "Планер А5 · Графітовий",
            "#494346",
            "#F1EFF0",
            "notebook"
        ),
        new(
            "demo-pens-set-3",
            "Набір кулькових ручок, 3 шт.",
            "Письмове приладдя",
            65.00m,
            30,
            true,
            "/uploads/products/prod_20261108170730_4e08e6762b9e4c8a855c1fd417636b69.png",
            "Набір із трьох ергономічних кулькових ручок із матовим корпусом та м’яким письмом без перепусток. Чорнило швидко висихає і не розмазується.",
            "Кількість у наборі: 3 шт.\nКолір чорнила: Синій\nТовщина лінії: 0.7 мм\nКорпус: Матовий пластик soft-touch\nМеханізм: Автоматичний кнопковий",
            "Набір ручок · 3 шт.",
            "#6D2038",
            "#F6EDF0",
            "pen"
        ),
        new(
            "demo-gel-pen-black-05",
            "Ручка гелева чорна 0.5 мм FineLine",
            "Письмове приладдя",
            42.00m,
            50,
            true,
            "/uploads/products/prod_20261108170545_ea896fa91a264a9f93986065130771ab.png",
            "Гелева ручка з голковим пишучим вузлом 0.5 мм для тонкого, чіткого письма, підписів і графічних схем.",
            "Тип: Гелева ручка\nКолір чорнила: Насичений чорний\nТовщина лінії: 0.5 мм\nГрип: Прогумований\nЗмінний стрижень: Так",
            "Гелева ручка 0.5 мм",
            "#3B2D32",
            "#F2EEF0",
            "pen"
        ),
        new(
            "demo-highlighters-pastel-4",
            "Набір текстових маркерів пастельних, 4 шт.",
            "Письмове приладдя",
            118.00m,
            15,
            true,
            "/uploads/products/prod_20261108170417_c6779bcd36024c3caa30d403c229d83f.png",
            "Комплект із чотирьох текстових маркерів у стриманих пастельних тонах (пудровий, пісочний, шавлієвий, бузковий). Чорнило на водній основі не проступає крізь папір.",
            "Кількість у наборі: 4 шт.\nНаконечник: Скошений клиноподібний\nТовщина лінії: 1–4.5 мм\nОснова чорнила: Водна",
            "Маркери пастельні · 4 шт.",
            "#9A5B6E",
            "#F9F1F4",
            "pen"
        ),
        new(
            "demo-mech-pencil-05",
            "Олівець механічний 0.5 мм зі змінними грифелями HB",
            "Письмове приладдя",
            84.00m,
            22,
            true,
            "/uploads/products/prod_20261108170311_624ff9c7418147a1b64b328b2ca2e955.png",
            "Надійний механічний олівець із металевою цангою, вбудованою гумкою та футляром із 12 запасними грифелями HB у комплекті.",
            "Діаметр грифеля: 0.5 мм\nТвердість: HB\nКорпус: Комбінований (пластик + метал)\nКомплектація: Олівець + пенал із 12 грифелями",
            "Механічний олівець 0.5",
            "#51172A",
            "#F4EAEB",
            "pen"
        ),
        new(
            "demo-graphite-pencils-6",
            "Набір чорнографітних олівців HB–2B, 6 шт.",
            "Письмове приладдя",
            76.00m,
            19,
            true,
            "/uploads/products/prod_20261108170147_cc368af34ec24a76955eae157943f433.png",
            "Шість дерев’яних шестигранних олівців різної м’якості для креслення, нарисів та письма. Стійкий до ударів графітовий стрижень.",
            "Кількість у наборі: 6 шт. (2×HB, 2×B, 2×2B)\nМатеріал корпусу: Кедрова деревина\nПокриття: Матовий лак бургунді\nФорма перерізу: Шестигранна",
            "Олівці графітні · 6 шт.",
            "#783348",
            "#F6EEF0",
            "pen"
        ),
        new(
            "demo-folder-sage-a4",
            "Папка на гумках А4, шавлієва",
            "Папки та архівація",
            89.00m,
            20,
            true,
            "/uploads/products/prod_20261108170054_9bdf1cda545341778db3cee0d4fdd128.png",
            "Містка пластикова папка формату А4 із бічними клапанами та еластичними кутовими гумками для охайного транспортування курсових робіт і документів.",
            "Формат: А4\nМісткість: До 250 аркушів\nМатеріал: Поліпропілен 550 мкм\nФіксація: Кутові еластичні гумки\nКолір: Шавлієвий",
            "Папка на гумках А4",
            "#7A8C78",
            "#F1F4F0",
            "folder"
        ),
        new(
            "demo-folder-sand-a4",
            "Папка для документів А4, пісочна",
            "Папки та архівація",
            75.00m,
            15,
            true,
            "/uploads/products/prod_20261108165959_d71ea0945e6a408ba188b50ea65e7f94.png",
            "Елегантна папка-конверт на кнопці з матового напівпрозорого пластику. Зручна для зберігання роздруківок, договорів та рефератів.",
            "Формат: А4+\nМісткість: До 120 аркушів\nЗастібка: Пластикова кнопка\nМатеріал: Матовий поліпропілен 400 мкм\nКолір: Пісочний",
            "Папка для документів А4",
            "#B39874",
            "#F8F4EE",
            "folder"
        ),
        new(
            "demo-binder-burgundy-a4",
            "Папка-реєстратор А4 арочна 50 мм, бордова",
            "Папки та архівація",
            145.00m,
            12,
            true,
            "/uploads/products/prod_20261108165826_be1ef98b4ade41e6a5a7dfcff8c22988.png",
            "Тверда архівна папка-сегрегатор із металевим арочним механізмом, кільцем на корінці та змінною інформаційною етикеткою.",
            "Формат: А4\nШирина корінця: 50 мм\nМісткість: До 350 аркушів\nМеханізм: Сталевий арочний із притискачем\nКолір: Бордовий (бургунді)",
            "Папка-реєстратор А4",
            "#6D2038",
            "#F5E8EC",
            "folder"
        ),
        new(
            "demo-expanding-folder-12",
            "Органайзер для документів А4 на 12 відділень",
            "Папки та архівація",
            185.00m,
            9,
            true,
            "/uploads/products/prod_20261108165658_c873a86d921d40a09b6dfb134da12b1e.png",
            "Портфель-гармошка на 12 секцій із кольоровими індексами-вкладками для зручного сортування дисциплін, рахунків або проєктної документації.",
            "Формат: А4\nКількість відділень: 12\nЗастібка: Еластичний шнур\nКомплектація: Набір наліпок-індексів\nКолір: Молочно-бежевий",
            "Папка-гармошка · 12 секцій",
            "#8A4B5E",
            "#F7F0F2",
            "folder"
        ),
        new(
            "demo-desk-organizer-mesh",
            "Органайзер настільний металевий, графіт",
            "Органайзери та настільне приладдя",
            210.00m,
            11,
            true,
            "/uploads/products/prod_20261108165528_2d5df66043a74118836866c1c580833c.png",
            "Сітчастий металевий органайзер із 4 відділеннями для ручок, маркерів, паперових блоків та дрібного канцелярського приладдя.",
            "Матеріал: Фарбована сталева сітка\nРозміри: 205×105×100 мм\nКількість секцій: 4 + висувна шухлядка\nНіжки: Нековзні гумові накладки\nКолір: Темний графіт",
            "Органайзер настільний",
            "#4A3E42",
            "#F2EFF0",
            "organizer"
        ),
        new(
            "demo-vertical-tray-milk",
            "Лоток вертикальний для паперів А4, молочний",
            "Органайзери та настільне приладдя",
            148.00m,
            14,
            true,
            "/uploads/products/prod_20261108165350_a8992a587c0a45608da027eca03fff0c.png",
            "Стійкий вертикальний накопичувач для журналів, папок, підручників та зошитів формату А4. Допомагає підтримувати порядок на робочому столі.",
            "Формат: А4\nРозміри: 250×95×310 мм\nМатеріал: Ударостійкий полістирол\nКолір: Молочний матовий",
            "Лоток вертикальний А4",
            "#8C6570",
            "#F9F4F5",
            "organizer"
        ),
        new(
            "demo-stapler-burgundy-24-6",
            "Степлер металевий №24/6 на 25 аркушів + скоби",
            "Органайзери та настільне приладдя",
            162.00m,
            17,
            true,
            "/uploads/products/prod_20261108165230_6cb39bd18ad549cbbabbaf41062fcee6.png",
            "Надійний настільний степлер із металевим механізмом, антиковзною основою та вбудованим антистеплером. У комплекті пачка скоб 1000 шт.",
            "Розмір скоб: №24/6 та №26/6\nГлибина прошивання: 60 мм\nПотужність: До 25 аркушів 80 г/м²\nКолір: Бургунді",
            "Степлер №24/6 + скоби",
            "#6D2038",
            "#F6EAEF",
            "organizer"
        ),
        new(
            "demo-clips-box-gold-80",
            "Набір біндерів та скріпок у боксі, 80 шт.",
            "Органайзери та настільне приладдя",
            92.00m,
            25,
            true,
            "/uploads/products/prod_20261108165004_107511bc438c4203a1d893562b3cfd5e.png",
            "Зручний прозорий органайзер-бокс із металевими біндерами (19 мм), скріпками (28 мм) та кнопками-цвяшками у золотисто-мідному кольорі.",
            "Кількість предметів: 80 шт. (15 біндерів, 45 скріпок, 20 кнопок)\nМатеріал: Нікельована сталь\nУпаковка: Багаторазовий пластиковий бокс",
            "Бокс біндерів і скріпок",
            "#9E6B55",
            "#F9F2EE",
            "organizer"
        ),
        new(
            "demo-office-paper-a4-500",
            "Папір офісний А4 80 г/м², 500 аркушів, клас А",
            "Папір та стікери",
            245.00m,
            28,
            true,
            "/uploads/products/prod_20261108164344_39e86cc244c64a34a54b444e029a9fb6.png",
            "Білосніжний офісний папір преміум-класу для лазерного та струменевого друку, двостороннього копіювання і чистового оформлення робіт.",
            "Формат: А4 (210×297 мм)\nЩільність: 80 г/м²\nКількість у пачці: 500 аркушів\nБілизна CIE: 168%\nКлас: А",
            "Папір офісний А4 · 500 арк.",
            "#6D2038",
            "#F4EDF0",
            "paper"
        ),
        new(
            "demo-sticky-notes-pastel-400",
            "Блок паперових стікерів із клейким шаром 75×75 мм, 400 арк.",
            "Папір та стікери",
            78.00m,
            30,
            true,
            "/uploads/products/prod_20261108164456_f9f77264c23040cab0da0c28ca109a57.png",
            "Кубик паперових нотаток із клейким краєм у теплих бежево-рожевих відтінках. Легко кріпляться до монітора, книги чи планера й не залишають слідів.",
            "Розмір: 75×75 мм\nКількість аркушів: 400\nЩільність паперу: 75 г/м²\nКольори: Молочний, рожево-бежевий, пудровий, пісочний",
            "Стікери 75×75 мм · 400 арк.",
            "#A15067",
            "#F8EEF1",
            "paper"
        ),
        // Приклад активного товару БЕЗ ЗАЛИШКУ (stock_quantity = 0) для перевірки стану "Немає в наявності"
        new(
            "demo-sketchbook-watercolor-a5",
            "Скетчбук для акварелі А5, 200 г/м² (немає в наявності)",
            "Креслення та творчість",
            260.00m,
            0,
            true,
            "/uploads/products/prod_20261108164643_fb73515393644357903e1bd3b503d23b.png",
            "Професійний скетчбук із фактурним акварельним папером щільністю 200 г/м² та прошитою палітуркою, що розкривається на 180 градусів.",
            "Формат: А5\nКількість аркушів: 40\nЩільність паперу: 200 г/м² (бавовна 25%)\nОбкладинка: Тверда полотняна\nПризначення: Акварель, лінери, гуаш",
            "Скетчбук акварельний А5",
            "#7D4254",
            "#F5ECF0",
            "art"
        ),
        // Приклад ПРИХОВАНОГО товару (is_active = false) для перевірки фільтрації та недоступності за прямою адресою
        new(
            "demo-ruler-metal-30",
            "Лінійка металева 30 см, матова сталь (прихована пропозиція)",
            "Креслення та творчість",
            58.00m,
            16,
            false,
            "/uploads/products/prod_20261108164742_8fb7233659724ae39ef02556e7739eeb.png",
            "Креслярська сталева лінійка довжиною 30 см із гравійованою міліметровою шкалою, що не стирається з часом.",
            "Довжина шкали: 30 см\nМатеріал: Нержавіюча матова сталь\nТовщина: 1.0 мм\nОсобливість: Двостороння розмітка з отвором для підвісу",
            "Лінійка сталева 30 см",
            "#5C4A50",
            "#F1EDEE",
            "art"
        ),
        new(
            "demo-diary-dated-burgundy-2026",
            "Щоденник датований А5 «папір. Бургунді 2026»",
            "Зошити та блокноти",
            280.00m,
            10,
            true,
            "/uploads/products/prod_20261108164854_a2026ef962f443e09e0e5f49534f9225.png",
            "Демонстраційний датований щоденник у бургундській твердій обкладинці, створений під час наскрізної перевірки.",
            "Формат: А5\nКількість сторінок: 352\nОбкладинка: Тверда бургунді\nПапір: Кремовий 80 г/м²",
            "Щоденник А5 · Бургунді",
            "#6D2038",
            "#F5EAEE",
            "notebook"
        )
    };
}
