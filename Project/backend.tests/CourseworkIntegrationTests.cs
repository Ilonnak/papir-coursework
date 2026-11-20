using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Backend.Configuration;
using Backend.Data;
using Backend.DTOs;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Backend.Tests;

/// <summary>
/// Детермінований перехоплювач транзакцій та збережень EF Core для контрольованого перекриття транзакцій
/// та перевірки справжнього відкату транзакції після фактичного запису в БД.
/// </summary>
public sealed class ControlledTransactionInterceptor : DbTransactionInterceptor
{
    private readonly Func<DbTransaction, Task>? _onCommittingAsync;

    public ControlledTransactionInterceptor(Func<DbTransaction, Task>? onCommittingAsync = null)
    {
        _onCommittingAsync = onCommittingAsync;
    }

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (_onCommittingAsync != null)
        {
            await _onCommittingAsync(transaction);
        }
        return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
    }
}

public sealed class ControlledDbCommandInterceptor : DbCommandInterceptor
{
    private readonly Func<DbCommand, Task>? _onBeforeExecuteAsync;
    private readonly Func<DbCommand, Task>? _onAfterExecuteAsync;

    public ControlledDbCommandInterceptor(
        Func<DbCommand, Task>? onBeforeExecuteAsync = null,
        Func<DbCommand, Task>? onAfterExecuteAsync = null)
    {
        _onBeforeExecuteAsync = onBeforeExecuteAsync;
        _onAfterExecuteAsync = onAfterExecuteAsync;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_onBeforeExecuteAsync != null)
        {
            await _onBeforeExecuteAsync(command);
        }
        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override async ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        var res = await base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        if (_onAfterExecuteAsync != null)
        {
            await _onAfterExecuteAsync(command);
        }
        return res;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (_onBeforeExecuteAsync != null)
        {
            await _onBeforeExecuteAsync(command);
        }
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        var reader = await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        if (_onAfterExecuteAsync != null)
        {
            await _onAfterExecuteAsync(command);
        }
        return reader;
    }
}

public class PostgresTestFixture : IAsyncLifetime
{
    public const string DedicatedTestDbName = "coursework_web_automated_tests";
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string TestConnectionString { get; private set; } = string.Empty;
    public string AdminBaseConnectionString { get; private set; } = string.Empty;
    public JwtSettings JwtConfig { get; private set; } = null!;
    public PickupPointSettings PickupConfig { get; private set; } = null!;

    public AppDbContext CreateIsolatedDbContext(params IInterceptor[] interceptors)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(TestConnectionString);

        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        return new AppDbContext(optionsBuilder.Options);
    }

    public async Task InitializeAsync()
    {
        var appSettingsPath = FindBackendAppSettingsPath();
        var config = new ConfigurationBuilder()
            .AddJsonFile(appSettingsPath, optional: false)
            .Build();

        var defaultCs = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not found in backend/appsettings.json");

        JwtConfig = config.GetSection("Jwt").Get<JwtSettings>()
            ?? throw new InvalidOperationException("Jwt config not found in backend/appsettings.json");

        PickupConfig = config.GetSection("PickupPoint").Get<PickupPointSettings>()
            ?? new PickupPointSettings();

        var csBuilder = new NpgsqlConnectionStringBuilder(defaultCs);

        // ЯВНА ПЕРЕВІРКА БЕЗПЕКИ: ніколи не дозволяємо тестовому фікстуру перезаписувати чи очищувати coursework_web або інші робочі БД
        if (!DedicatedTestDbName.Equals("coursework_web_automated_tests", StringComparison.Ordinal) ||
            DedicatedTestDbName.Equals(csBuilder.Database, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SAFETY CHECK FAILED: Test database name must be strictly isolated from coursework_web.");
        }

        var adminBuilder = new NpgsqlConnectionStringBuilder(defaultCs)
        {
            Database = "postgres"
        };
        AdminBaseConnectionString = adminBuilder.ConnectionString;

        var testBuilder = new NpgsqlConnectionStringBuilder(defaultCs)
        {
            Database = DedicatedTestDbName
        };
        TestConnectionString = testBuilder.ConnectionString;

        await RecreateIsolatedTestDatabaseAsync();

        var backendContentRoot = Path.GetDirectoryName(appSettingsPath)!;

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(backendContentRoot);
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(TestConnectionString));
            });
        });

        using var client = Factory.CreateClient();
        var health = await client.GetAsync("/api/health");
        health.EnsureSuccessStatusCode();
    }

    private async Task RecreateIsolatedTestDatabaseAsync()
    {
        if (DedicatedTestDbName != "coursework_web_automated_tests")
        {
            throw new InvalidOperationException("Refusing to drop any database other than coursework_web_automated_tests.");
        }

        await using var conn = new NpgsqlConnection(AdminBaseConnectionString);
        await conn.OpenAsync();

        await using (var cmdTerm = new NpgsqlCommand($@"
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = '{DedicatedTestDbName}' AND pid <> pg_backend_pid();", conn))
        {
            await cmdTerm.ExecuteNonQueryAsync();
        }

        await using (var cmdDrop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DedicatedTestDbName}\";", conn))
        {
            await cmdDrop.ExecuteNonQueryAsync();
        }

        await using (var cmdCreate = new NpgsqlCommand($"CREATE DATABASE \"{DedicatedTestDbName}\";", conn))
        {
            await cmdCreate.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (Factory != null)
        {
            await Factory.DisposeAsync();
        }

        if (DedicatedTestDbName == "coursework_web_automated_tests" && !string.IsNullOrEmpty(AdminBaseConnectionString))
        {
            try
            {
                await using var conn = new NpgsqlConnection(AdminBaseConnectionString);
                await conn.OpenAsync();
                await using var cmdTerm = new NpgsqlCommand($@"
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = '{DedicatedTestDbName}' AND pid <> pg_backend_pid();", conn);
                await cmdTerm.ExecuteNonQueryAsync();

                await using var cmdDrop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DedicatedTestDbName}\";", conn);
                await cmdDrop.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ігноруємо помилки при фінальному прибиранні тестової БД
            }
        }
    }

    private static string FindBackendAppSettingsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "appsettings.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate backend/appsettings.json from test runner directory.");
    }
}

public class CourseworkIntegrationTests : IClassFixture<PostgresTestFixture>
{
    private readonly PostgresTestFixture _fixture;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public CourseworkIntegrationTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient CreateClientWithToken(string? token = null)
    {
        var client = _fixture.Factory.CreateClient();
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private async Task<AuthResponseDto> LoginAsync(string email, string password)
    {
        using var client = CreateClientWithToken();
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOpts);
        return body!;
    }

    private async Task<AuthResponseDto> RegisterFreshBuyerAsync(string emailPrefix)
    {
        using var client = CreateClientWithToken();
        var email = $"{emailPrefix}_{Guid.NewGuid():N}@papir.local";
        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequestDto
        {
            Email = email,
            Password = "BuyerPass!2026",
            FirstName = "Тест",
            LastName = "Покупець"
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOpts))!;
    }

    [Fact(DisplayName = "1. Гість бачить каталог, але не має доступу до адміністративних даних (401/403)")]
    public async Task Check01_GuestSeesCatalogButNotAdminEndpoints()
    {
        using var client = CreateClientWithToken();

        var catRes = await client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, catRes.StatusCode);

        var catalog = await catRes.Content.ReadFromJsonAsync<PagedResultDto<ProductDto>>(JsonOpts);
        Assert.NotNull(catalog);
        Assert.NotEmpty(catalog!.Items);
        Assert.All(catalog.Items, p => Assert.True(p.IsActive));

        var adminProductsRes = await client.GetAsync("/api/admin/products");
        Assert.Equal(HttpStatusCode.Unauthorized, adminProductsRes.StatusCode);

        var adminOrdersRes = await client.GetAsync("/api/admin/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, adminOrdersRes.StatusCode);
    }

    [Fact(DisplayName = "2. Публічна реєстрація ніколи не дозволяє підвищити роль до адміністратора + case-insensitive унікальність email")]
    public async Task Check02_RegistrationNeverElevatesRoleAndEnforcesCaseInsensitiveEmail()
    {
        using var client = CreateClientWithToken();

        var maliciousJson = JsonSerializer.Serialize(new
        {
            email = "NewStudent@Papir.Local",
            password = "SafePassword123!",
            firstName = "Марія",
            lastName = "Шевченко",
            isAdmin = true,
            role = "Admin"
        });

        var res = await client.PostAsync("/api/auth/register", new StringContent(maliciousJson, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var auth = await res.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOpts);
        Assert.NotNull(auth);
        Assert.False(auth!.User.IsAdmin);
        Assert.Equal("Buyer", auth.User.Role);

        using var buyerClient = CreateClientWithToken(auth.Token);
        var adminAttempt = await buyerClient.GetAsync("/api/admin/products");
        Assert.Equal(HttpStatusCode.Forbidden, adminAttempt.StatusCode);

        var dupRes = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequestDto
        {
            Email = "newstudent@papir.local",
            Password = "AnotherPassword123!",
            FirstName = "Марія",
            LastName = "Шевченко"
        });
        Assert.Equal(HttpStatusCode.Conflict, dupRes.StatusCode);
    }

    [Fact(DisplayName = "3. Неправильний пароль і прострочений JWT токен відхиляються")]
    public async Task Check03_WrongPasswordAndExpiredTokenRejected()
    {
        using var client = CreateClientWithToken();

        var wrongLogin = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = "olena@papir.local",
            Password = "WrongPassword999"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongLogin.StatusCode);

        var keyBytes = Encoding.UTF8.GetBytes(_fixture.JwtConfig.Key);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
        var expiredTokenDescriptor = new JwtSecurityToken(
            issuer: _fixture.JwtConfig.Issuer,
            audience: _fixture.JwtConfig.Audience,
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "2"),
                new Claim(ClaimTypes.Role, "Buyer")
            },
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddMinutes(-10),
            signingCredentials: credentials);

        var expiredJwt = new JwtSecurityTokenHandler().WriteToken(expiredTokenDescriptor);

        using var expiredClient = CreateClientWithToken(expiredJwt);
        var meRes = await expiredClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meRes.StatusCode);
    }

    [Fact(DisplayName = "4. Покупець не читає і не змінює кошик та замовлення іншого покупця через прямі запити")]
    public async Task Check04_BuyerIsolationForCartAndOrders()
    {
        var olenaAuth = await LoginAsync("olena@papir.local", "BuyerDemo!2026");
        var andriiAuth = await LoginAsync("andrii@papir.local", "BuyerDemo!2026");

        using var olenaClient = CreateClientWithToken(olenaAuth.Token);
        using var andriiClient = CreateClientWithToken(andriiAuth.Token);

        var olenaOrders = await olenaClient.GetFromJsonAsync<List<OrderDto>>("/api/orders", JsonOpts);
        Assert.NotNull(olenaOrders);
        Assert.NotEmpty(olenaOrders!);
        Assert.All(olenaOrders, o => Assert.Equal(olenaAuth.User.Id, o.UserId));

        var olenaOrderId = olenaOrders[0].Id;
        var directAccessRes = await andriiClient.GetAsync($"/api/orders/{olenaOrderId}");
        Assert.Equal(HttpStatusCode.Forbidden, directAccessRes.StatusCode);

        var statusChangeAttempt = await olenaClient.PatchAsJsonAsync($"/api/admin/orders/{olenaOrderId}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Completed
        });
        Assert.Equal(HttpStatusCode.Forbidden, statusChangeAttempt.StatusCode);
    }

    [Fact(DisplayName = "5. Неактивний товар недоступний гостю і покупцю також за прямою адресою, а товар без залишку не додається до кошика")]
    public async Task Check05_InactiveProductNotAccessibleAndZeroStockCannotBeAdded()
    {
        var adminAuth = await LoginAsync("admin@papir.local", "AdminDemo!2026");
        var buyerAuth = await LoginAsync("olena@papir.local", "BuyerDemo!2026");

        using var adminClient = CreateClientWithToken(adminAuth.Token);
        using var buyerClient = CreateClientWithToken(buyerAuth.Token);
        using var guestClient = CreateClientWithToken();

        var allAdminProducts = await adminClient.GetFromJsonAsync<List<ProductDto>>("/api/admin/products", JsonOpts);
        Assert.NotNull(allAdminProducts);

        var hiddenProduct = allAdminProducts!.First(p => !p.IsActive);
        var zeroStockProduct = allAdminProducts.First(p => p.IsActive && p.StockQuantity == 0);

        var guestDirect = await guestClient.GetAsync($"/api/products/{hiddenProduct.Id}");
        Assert.Equal(HttpStatusCode.NotFound, guestDirect.StatusCode);

        var buyerDirect = await buyerClient.GetAsync($"/api/products/{hiddenProduct.Id}");
        Assert.Equal(HttpStatusCode.NotFound, buyerDirect.StatusCode);

        var addHidden = await buyerClient.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto
        {
            ProductId = hiddenProduct.Id,
            Quantity = 1
        });
        Assert.Equal(HttpStatusCode.NotFound, addHidden.StatusCode);

        var addZeroStock = await buyerClient.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto
        {
            ProductId = zeroStockProduct.Id,
            Quantity = 1
        });
        Assert.Equal(HttpStatusCode.Conflict, addZeroStock.StatusCode);
    }

    [Fact(DisplayName = "6. Від’ємні ціни, від’ємні залишки, некоректні кількості та каскадне видалення непорожньої категорії відхиляються")]
    public async Task Check06_NegativePricesStocksInvalidQuantitiesAndNonEmptyCategoryDeleteRejected()
    {
        var adminAuth = await LoginAsync("admin@papir.local", "AdminDemo!2026");
        var buyerAuth = await LoginAsync("olena@papir.local", "BuyerDemo!2026");

        using var adminClient = CreateClientWithToken(adminAuth.Token);
        using var buyerClient = CreateClientWithToken(buyerAuth.Token);

        var negPriceRes = await adminClient.PostAsJsonAsync("/api/admin/products", new SaveProductRequestDto
        {
            CategoryId = 1,
            Name = "Некоректний товар",
            Description = "Опис",
            Specifications = "Характеристики",
            Price = -10m,
            StockQuantity = 5,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, negPriceRes.StatusCode);

        var negStockRes = await adminClient.PostAsJsonAsync("/api/admin/products", new SaveProductRequestDto
        {
            CategoryId = 1,
            Name = "Некоректний залишок",
            Description = "Опис",
            Specifications = "Характеристики",
            Price = 50m,
            StockQuantity = -3,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, negStockRes.StatusCode);

        var zeroQtyRes = await buyerClient.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto
        {
            ProductId = 1,
            Quantity = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, zeroQtyRes.StatusCode);

        var deleteNonEmptyCat = await adminClient.DeleteAsync("/api/admin/categories/1");
        Assert.Equal(HttpStatusCode.Conflict, deleteNonEmptyCat.StatusCode);
    }

    [Fact(DisplayName = "7. Пошук, фільтр за категорією та діапазоном ціни і сортування працюють узгоджено разом")]
    public async Task Check07_CombinedCatalogFiltersWorkTogether()
    {
        using var client = CreateClientWithToken();

        var categories = await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", JsonOpts);
        var notebooksCat = categories!.First(c => c.Name.Contains("Зошити та блокноти"));

        var res = await client.GetAsync($"/api/products?categoryId={notebooksCat.Id}&search=Блокнот&minPrice=100&maxPrice=160&sort=price_desc");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var data = await res.Content.ReadFromJsonAsync<PagedResultDto<ProductDto>>(JsonOpts);
        Assert.NotNull(data);
        Assert.NotEmpty(data!.Items);
        Assert.All(data.Items, p =>
        {
            Assert.Equal(notebooksCat.Id, p.CategoryId);
            Assert.Contains("блокнот", p.Name, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(p.Price, 100m, 160m);
        });

        for (int i = 1; i < data.Items.Count; i++)
        {
            Assert.True(data.Items[i - 1].Price >= data.Items[i].Price);
        }
    }

    [Fact(DisplayName = "8-11, 13, 16-18. Життєвий цикл замовлення, компенсаційні зміни цін, обов’язковий IdempotencyKey (повтор та зміна payload), незмінність історії, узгодженість оплати й скасування")]
    public async Task Check08To11_13_16To18_OrderLifecyclePriceCompensationIdempotencyAndPaymentRules()
    {
        var adminAuth = await LoginAsync("admin@papir.local", "AdminDemo!2026");
        using var adminClient = CreateClientWithToken(adminAuth.Token);

        var buyerAuth = await RegisterFreshBuyerAsync("lifecycle_buyer");
        using var buyerClient = CreateClientWithToken(buyerAuth.Token);

        // Створюємо два товари по 100 грн
        var p1Res = await adminClient.PostAsJsonAsync("/api/admin/products", new SaveProductRequestDto
        {
            CategoryId = 1,
            Name = "Записник курсової №1",
            Description = "Опис 1",
            Specifications = "Формат: А5",
            Price = 100.00m,
            StockQuantity = 10,
            IsActive = true
        });
        var prod1 = (await p1Res.Content.ReadFromJsonAsync<ProductDto>(JsonOpts))!;

        var p2Res = await adminClient.PostAsJsonAsync("/api/admin/products", new SaveProductRequestDto
        {
            CategoryId = 1,
            Name = "Записник курсової №2",
            Description = "Опис 2",
            Specifications = "Формат: А5",
            Price = 100.00m,
            StockQuantity = 10,
            IsActive = true
        });
        var prod2 = (await p2Res.Content.ReadFromJsonAsync<ProductDto>(JsonOpts))!;

        // Порожній кошик не оформлюється
        var emptyOrderRes = await buyerClient.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            RecipientFirstName = "Тарас",
            RecipientLastName = "Мельник",
            RecipientPhone = "+380509998877",
            RecipientEmail = buyerAuth.User.Email,
            CartSnapshotToken = "dummy-token",
            IdempotencyKey = "idemp-empty-cart"
        });
        Assert.Equal(HttpStatusCode.BadRequest, emptyOrderRes.StatusCode);

        // Додаємо по 1 шт. обох товарів (загальна сума = 200 грн)
        await buyerClient.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto { ProductId = prod1.Id, Quantity = 1 });
        await buyerClient.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto { ProductId = prod2.Id, Quantity = 1 });

        // Кошик відновлюється після повторного входу
        var reloginAuth = await LoginAsync(buyerAuth.User.Email, "BuyerPass!2026");
        using var reloginClient = CreateClientWithToken(reloginAuth.Token);
        var initialCart = await reloginClient.GetFromJsonAsync<CartResponseDto>("/api/cart", JsonOpts);
        Assert.NotNull(initialCart);
        Assert.Equal(2, initialCart!.Items.Count);
        Assert.Equal(200.00m, initialCart.TotalAmount);
        Assert.False(string.IsNullOrWhiteSpace(initialCart.CartSnapshotToken));

        // Перевірка 4 (компенсаційна зміна цін):
        // Адміністратор змінює ціну Товару 1 зі 100 на 80 грн (-20 грн), а Товару 2 зі 100 на 120 грн (+20 грн).
        // Загальна сума залишається 200.00 грн, але ціни окремих позицій змінилися!
        await adminClient.PutAsJsonAsync($"/api/admin/products/{prod1.Id}", new SaveProductRequestDto
        {
            CategoryId = prod1.CategoryId,
            Name = prod1.Name,
            Description = prod1.Description,
            Specifications = prod1.Specifications,
            Price = 80.00m,
            StockQuantity = 10,
            Version = prod1.Version,
            IsActive = true,
            ImagePath = prod1.ImagePath
        });

        await adminClient.PutAsJsonAsync($"/api/admin/products/{prod2.Id}", new SaveProductRequestDto
        {
            CategoryId = prod2.CategoryId,
            Name = prod2.Name,
            Description = prod2.Description,
            Specifications = prod2.Specifications,
            Price = 120.00m,
            StockQuantity = 10,
            Version = prod2.Version,
            IsActive = true,
            ImagePath = prod2.ImagePath
        });

        // Покупець намагається оформити зі старим CartSnapshotToken (хоча ExpectedTotalAmount = 200.00 збігається) -> 409 Conflict!
        var compensatingPriceOrder = await reloginClient.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            RecipientFirstName = "Тарас",
            RecipientLastName = "Мельник",
            RecipientPhone = "+380509998877",
            RecipientEmail = buyerAuth.User.Email,
            CartSnapshotToken = initialCart.CartSnapshotToken,
            ExpectedTotalAmount = 200.00m,
            IdempotencyKey = "idemp-compensating-price"
        });
        Assert.Equal(HttpStatusCode.Conflict, compensatingPriceOrder.StatusCode);

        // Оновлюємо кошик і отримуємо новий CartSnapshotToken
        var refreshedCart = await reloginClient.GetFromJsonAsync<CartResponseDto>("/api/cart", JsonOpts);
        Assert.NotEqual(initialCart.CartSnapshotToken, refreshedCart!.CartSnapshotToken);

        // Перевірка обов'язковості IdempotencyKey (порожній ключ відхиляється 400)
        var missingIdempRes = await reloginClient.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            RecipientFirstName = "Тарас",
            RecipientLastName = "Мельник",
            RecipientPhone = "+380509998877",
            RecipientEmail = buyerAuth.User.Email,
            CartSnapshotToken = refreshedCart.CartSnapshotToken,
            ExpectedTotalAmount = 200.00m,
            IdempotencyKey = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingIdempRes.StatusCode);

        // Успішне оформлення з актуальним CartSnapshotToken та IdempotencyKey
        const string validIdempKey = "idemp-taras-valid-001";
        var validOrderReq = new CreateOrderRequestDto
        {
            RecipientFirstName = "Тарас",
            RecipientLastName = "Мельник",
            RecipientPhone = "+380509998877",
            RecipientEmail = buyerAuth.User.Email,
            Comment = "Оформлення з актуальним відбитком кошика",
            CartSnapshotToken = refreshedCart.CartSnapshotToken,
            ExpectedTotalAmount = 200.00m,
            IdempotencyKey = validIdempKey
        };

        var orderSuccessRes = await reloginClient.PostAsJsonAsync("/api/orders", validOrderReq);
        Assert.Equal(HttpStatusCode.Created, orderSuccessRes.StatusCode);
        var createdOrder = (await orderSuccessRes.Content.ReadFromJsonAsync<OrderDto>(JsonOpts))!;
        Assert.Equal(OrderStatus.New, createdOrder.Status);
        Assert.Equal(200.00m, createdOrder.TotalAmount);

        // Повтор того самого запиту з тим самим IdempotencyKey та тим самим змістом повертає те саме замовлення (200 OK)
        var retrySamePayloadRes = await reloginClient.PostAsJsonAsync("/api/orders", validOrderReq);
        Assert.Equal(HttpStatusCode.OK, retrySamePayloadRes.StatusCode);
        var retryOrder = (await retrySamePayloadRes.Content.ReadFromJsonAsync<OrderDto>(JsonOpts))!;
        Assert.Equal(createdOrder.Id, retryOrder.Id);

        // Повторне використання того самого IdempotencyKey з ІНШИМ змістом запиту повертає 409 Conflict (IDEMPOTENCY_KEY_PAYLOAD_MISMATCH)
        var mismatchedPayloadReq = new CreateOrderRequestDto
        {
            RecipientFirstName = "ІншийОтримувач",
            RecipientLastName = "Мельник",
            RecipientPhone = "+380509998877",
            RecipientEmail = buyerAuth.User.Email,
            Comment = "Інший зміст із тим самим ключем",
            CartSnapshotToken = refreshedCart.CartSnapshotToken,
            ExpectedTotalAmount = 200.00m,
            IdempotencyKey = validIdempKey
        };
        var mismatchRes = await reloginClient.PostAsJsonAsync("/api/orders", mismatchedPayloadReq);
        Assert.Equal(HttpStatusCode.Conflict, mismatchRes.StatusCode);
        var mismatchJson = await mismatchRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IDEMPOTENCY_KEY_PAYLOAD_MISMATCH", mismatchJson.GetProperty("code").GetString());

        // Перевірка 3 (Узгодженість оплати та скасування):
        // Спроба зафіксувати оплату для замовлення у статусі «Нове» до прийняття в обробку відхиляється (409)
        var payWhileNewRes = await adminClient.PatchAsync($"/api/admin/orders/{createdOrder.Id}/payment", null);
        Assert.Equal(HttpStatusCode.Conflict, payWhileNewRes.StatusCode);

        // Переводимо у «В обробці»
        var toProcRes = await adminClient.PatchAsJsonAsync($"/api/admin/orders/{createdOrder.Id}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Processing
        });
        Assert.Equal(HttpStatusCode.OK, toProcRes.StatusCode);
        var procDto = (await toProcRes.Content.ReadFromJsonAsync<OrderDto>(JsonOpts))!;
        Assert.Contains(OrderStatus.Cancelled, procDto.AllowedTransitions);
        Assert.Contains(OrderStatus.Completed, procDto.AllowedTransitions);

        // Фіксуємо оплату окремою дією для замовлення «В обробці»
        var markPaidRes = await adminClient.PatchAsync($"/api/admin/orders/{createdOrder.Id}/payment", null);
        Assert.Equal(HttpStatusCode.OK, markPaidRes.StatusCode);
        var paidDto = (await markPaidRes.Content.ReadFromJsonAsync<OrderDto>(JsonOpts))!;
        Assert.True(paidDto.IsPaid);

        // Після оплати перехід у «Скасоване» виключено з AllowedTransitions і заборонено сервером (409 CANNOT_CANCEL_PAID_ORDER)
        Assert.DoesNotContain(OrderStatus.Cancelled, paidDto.AllowedTransitions);
        Assert.Single(paidDto.AllowedTransitions, OrderStatus.Completed);

        var cancelPaidRes = await adminClient.PatchAsJsonAsync($"/api/admin/orders/{createdOrder.Id}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Cancelled
        });
        Assert.Equal(HttpStatusCode.Conflict, cancelPaidRes.StatusCode);
        var cancelPaidErr = await cancelPaidRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CANNOT_CANCEL_PAID_ORDER", cancelPaidErr.GetProperty("code").GetString());

        // Завершуємо видачу («В обробці» -> «Виконане»)
        var completeRes = await adminClient.PatchAsJsonAsync($"/api/admin/orders/{createdOrder.Id}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Completed
        });
        Assert.Equal(HttpStatusCode.OK, completeRes.StatusCode);
    }

    [Fact(DisplayName = "P1. Детерміновані тести узгодженості кошика та оформлення (перекриття транзакцій: 2 одночасні створення позиції, 2 одночасні збільшення кількості, зміна кількості під час оформлення)")]
    public async Task CheckP1_CartAndOrderConcurrencyWithControlledOverlap()
    {
        var adminAuth = await LoginAsync("admin@papir.local", "AdminDemo!2026");
        using var adminClient = CreateClientWithToken(adminAuth.Token);

        var prodRes = await adminClient.PostAsJsonAsync("/api/admin/products", new SaveProductRequestDto
        {
            CategoryId = 1,
            Name = "Товар для перевірки конкурентності кошика",
            Description = "Опис",
            Specifications = "Тест: кошик",
            Price = 50.00m,
            StockQuantity = 30,
            IsActive = true
        });
        var prod = (await prodRes.Content.ReadFromJsonAsync<ProductDto>(JsonOpts))!;

        // Підсценарій 1А: Два конкурентні AddItemAsync для ОДНІЄЇ І ТІЄЇ САМОЇ нової позиції кошика з контрольованим перекриттям.
        // Перший потік захоплює FOR UPDATE на користувачі, читає відсутність позиції в cart_items, робить INSERT у межах транзакції і перед COMMIT сигналізує другому потоку.
        // Другий потік стартує і гарантовано чекає на FOR UPDATE, не падаючи з помилкою унікальності PK (user_id, product_id) і не втрачаючи збільшення.
        var buyerA = await RegisterFreshBuyerAsync("cart_race_new");
        var firstEnteredLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var interceptor1 = new ControlledTransactionInterceptor(
            onCommittingAsync: async _ =>
            {
                firstEnteredLock.TrySetResult();
                await allowFirstToFinish.Task;
            });

        await using var ctx1 = _fixture.CreateIsolatedDbContext(interceptor1);
        await using var ctx2 = _fixture.CreateIsolatedDbContext();

        var service1 = new CartService(ctx1);
        var service2 = new CartService(ctx2);

        var taskAdd1 = Task.Run(() => service1.AddItemAsync(buyerA.User.Id, new AddToCartRequestDto { ProductId = prod.Id, Quantity = 2 }));
        await firstEnteredLock.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Запускаємо другий запит додавання (+3 шт.) у момент, коли перший вже всередині транзакції після INSERT cart_items і перед COMMIT
        var taskAdd2 = Task.Run(() => service2.AddItemAsync(buyerA.User.Id, new AddToCartRequestDto { ProductId = prod.Id, Quantity = 3 }));
        await Task.Delay(120); // Даємо другому запиту дійти до блокування SELECT id FROM users FOR UPDATE
        allowFirstToFinish.TrySetResult();

        var addResults = await Task.WhenAll(taskAdd1, taskAdd2).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.All(addResults, r => Assert.True(r.Success, r.ErrorMessage));

        // Перевіряємо через новий DbContext: рівно один запис у кошику з сумарною кількістю 2 + 3 = 5 шт. (жодної помилки PK і жодної втрати інкременту!)
        await using (var verifyCtx = _fixture.CreateIsolatedDbContext())
        {
            var itemInDb = await verifyCtx.CartItems.SingleAsync(ci => ci.UserId == buyerA.User.Id && ci.ProductId == prod.Id);
            Assert.Equal(5, itemInDb.Quantity);
        }

        // Підсценарій 1А-bis: Два одночасні збільшення кількості (AddItemAsync) вже наявної позиції з контрольованим перекриттям
        var firstIncrementEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstIncrementToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var interceptorInc = new ControlledTransactionInterceptor(
            onCommittingAsync: async _ =>
            {
                firstIncrementEntered.TrySetResult();
                await allowFirstIncrementToFinish.Task;
            });

        await using var ctxInc1 = _fixture.CreateIsolatedDbContext(interceptorInc);
        await using var ctxInc2 = _fixture.CreateIsolatedDbContext();

        var svcInc1 = new CartService(ctxInc1);
        var svcInc2 = new CartService(ctxInc2);

        var taskInc1 = Task.Run(() => svcInc1.AddItemAsync(buyerA.User.Id, new AddToCartRequestDto { ProductId = prod.Id, Quantity = 2 }));
        await firstIncrementEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var taskInc2 = Task.Run(() => svcInc2.AddItemAsync(buyerA.User.Id, new AddToCartRequestDto { ProductId = prod.Id, Quantity = 4 }));
        await Task.Delay(120);
        allowFirstIncrementToFinish.TrySetResult();

        var incResults = await Task.WhenAll(taskInc1, taskInc2).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.All(incResults, r => Assert.True(r.Success, r.ErrorMessage));

        await using (var verifyCtx2 = _fixture.CreateIsolatedDbContext())
        {
            var itemInDb2 = await verifyCtx2.CartItems.SingleAsync(ci => ci.UserId == buyerA.User.Id && ci.ProductId == prod.Id);
            Assert.Equal(11, itemInDb2.Quantity); // 5 + 2 + 4 = 11 шт. без втрати інкременту
        }

        // Підсценарій 1Б: Зміна кількості в кошику (UpdateItemQuantityAsync) під час оформлення замовлення (CreateOrderAsync).
        // Завдяки єдиному порядку блокування на рядку users та перевірці CartSnapshotToken:
        // ніколи не втрачається зміна кількості і ніколи не списується непідтверджена кількість.
        var buyerB = await RegisterFreshBuyerAsync("cart_order_overlap");
        using var buyerBClient = CreateClientWithToken(buyerB.Token);

        await buyerBClient.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto { ProductId = prod.Id, Quantity = 2 });
        var cartBefore = await buyerBClient.GetFromJsonAsync<CartResponseDto>("/api/cart", JsonOpts);
        Assert.Equal(2, cartBefore!.TotalQuantity);

        var updateEnteredLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowUpdateToCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var interceptorUpdate = new ControlledTransactionInterceptor(
            onCommittingAsync: async _ =>
            {
                updateEnteredLock.TrySetResult();
                await allowUpdateToCommit.Task;
            });

        await using var updateCtx = _fixture.CreateIsolatedDbContext(interceptorUpdate);
        await using var orderCtx = _fixture.CreateIsolatedDbContext();

        var cartSvcForOverlap = new CartService(updateCtx);
        var orderSvcForOverlap = new OrderService(orderCtx, Options.Create(_fixture.PickupConfig));

        // 1. Запускаємо зміну кількості в кошику з 2 на 4 шт. і утримуємо транзакцію після UPDATE cart_items
        var updateTask = Task.Run(() => cartSvcForOverlap.UpdateItemQuantityAsync(buyerB.User.Id, prod.Id, new UpdateCartItemRequestDto { Quantity = 4 }));
        await updateEnteredLock.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // 2. Одночасно запускаємо оформлення замовлення зі старим CartSnapshotToken (який був для 2 шт.)
        var orderTask = Task.Run(() => orderSvcForOverlap.CreateOrderAsync(buyerB.User.Id, new CreateOrderRequestDto
        {
            RecipientFirstName = "Олег",
            RecipientLastName = "Коваленко",
            RecipientPhone = "+380501234567",
            RecipientEmail = buyerB.User.Email,
            CartSnapshotToken = cartBefore.CartSnapshotToken,
            ExpectedTotalAmount = cartBefore.TotalAmount,
            IdempotencyKey = "idemp-overlap-check-1"
        }));

        await Task.Delay(120); // CreateOrderAsync блокується на SELECT id FROM users FOR UPDATE
        allowUpdateToCommit.TrySetResult();

        var updateRes = await updateTask.WaitAsync(TimeSpan.FromSeconds(10));
        var orderRes = await orderTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(updateRes.Success);
        Assert.False(orderRes.Success);
        Assert.Equal(409, orderRes.StatusCode);
        Assert.Equal("CART_OR_PRICE_CHANGED", orderRes.ErrorCode);

        // Кошик зберіг нову кількість 4 шт. і не був помилково очищений або оформлений на 2 чи 4 шт. без підтвердження
        var cartAfterOverlap = await buyerBClient.GetFromJsonAsync<CartResponseDto>("/api/cart", JsonOpts);
        Assert.Equal(4, cartAfterOverlap!.TotalQuantity);
    }

    [Fact(DisplayName = "P2. Захист залишку від застарілої форми адміністратора: адмін відкрив товар із залишком 5 -> покупець придбав 1 -> адмін зберігає стару форму -> 409 Conflict і залишок залишається 4")]
    public async Task CheckP2_StaleAdminProductFormDoesNotOverwriteSoldStock()
    {
        var adminAuth = await LoginAsync("admin@papir.local", "AdminDemo!2026");
        using var adminClient = CreateClientWithToken(adminAuth.Token);

        var buyerAuth = await RegisterFreshBuyerAsync("stale_form_buyer");
        using var buyerClient = CreateClientWithToken(buyerAuth.Token);

        // 1. Адміністратор створює товар із залишком 5 шт.
        var createRes = await adminClient.PostAsJsonAsync("/api/admin/products", new SaveProductRequestDto
        {
            CategoryId = 1,
            Name = "Органайзер для перевірки застарілої форми",
            Description = "Початковий опис товару",
            Specifications = "Секцій: 4",
            Price = 150.00m,
            StockQuantity = 5,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var adminOpenedProduct = (await createRes.Content.ReadFromJsonAsync<ProductDto>(JsonOpts))!;
        Assert.Equal(5, adminOpenedProduct.StockQuantity);
        Assert.Equal(1, adminOpenedProduct.Version);

        // 2. Поки форма відкрита у браузері адміна (StockQuantity = 5, Version = 1), покупець купує 1 шт.
        await buyerClient.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto { ProductId = adminOpenedProduct.Id, Quantity = 1 });
        var buyerCart = await buyerClient.GetFromJsonAsync<CartResponseDto>("/api/cart", JsonOpts);

        var orderRes = await buyerClient.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            RecipientFirstName = "Ірина",
            RecipientLastName = "Покупець",
            RecipientPhone = "+380501112233",
            RecipientEmail = buyerAuth.User.Email,
            CartSnapshotToken = buyerCart!.CartSnapshotToken,
            ExpectedTotalAmount = 150.00m,
            IdempotencyKey = "idemp-buy-1-from-5"
        });
        Assert.Equal(HttpStatusCode.Created, orderRes.StatusCode);

        // Залишок у БД тепер 4 шт., а версія товару = 2
        var currentProdInCatalog = await buyerClient.GetFromJsonAsync<ProductDto>($"/api/products/{adminOpenedProduct.Id}", JsonOpts);
        Assert.Equal(4, currentProdInCatalog!.StockQuantity);
        Assert.Equal(2, currentProdInCatalog.Version);

        // 3. Адміністратор редагує опис у своїй старій формі (де досі стоїть StockQuantity = 5 та Version = 1) і натискає «Зберегти»
        var staleSaveRes = await adminClient.PutAsJsonAsync($"/api/admin/products/{adminOpenedProduct.Id}", new SaveProductRequestDto
        {
            CategoryId = adminOpenedProduct.CategoryId,
            Name = adminOpenedProduct.Name,
            Description = "Оновлений опис зі старої форми",
            Specifications = adminOpenedProduct.Specifications,
            Price = adminOpenedProduct.Price,
            StockQuantity = 5, // застарілий залишок із браузера!
            Version = adminOpenedProduct.Version, // застаріла версія 1!
            IsActive = true,
            ImagePath = adminOpenedProduct.ImagePath
        });

        // Сервер повертає 409 Conflict з актуальними даними товару (StockQuantity = 4, Version = 2)
        Assert.Equal(HttpStatusCode.Conflict, staleSaveRes.StatusCode);
        var conflictPayload = await staleSaveRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PRODUCT_CONCURRENCY_CONFLICT", conflictPayload.GetProperty("code").GetString());
        var freshDataFromConflict = conflictPayload.GetProperty("data").Deserialize<ProductDto>(JsonOpts)!;
        Assert.Equal(4, freshDataFromConflict.StockQuantity);
        Assert.Equal(2, freshDataFromConflict.Version);

        // Перевіряємо, що залишок у БД НЕ повернувся до 5, а залишився 4
        var verifyProdAfterConflict = await buyerClient.GetFromJsonAsync<ProductDto>($"/api/products/{adminOpenedProduct.Id}", JsonOpts);
        Assert.Equal(4, verifyProdAfterConflict!.StockQuantity);

        // 4. Адміністратор повторно підтверджує збереження вже з оновленою версією (Version = 2, StockQuantity = 4) -> 200 OK
        var confirmedSaveRes = await adminClient.PutAsJsonAsync($"/api/admin/products/{adminOpenedProduct.Id}", new SaveProductRequestDto
        {
            CategoryId = freshDataFromConflict.CategoryId,
            Name = freshDataFromConflict.Name,
            Description = "Оновлений опис після повторного підтвердження",
            Specifications = freshDataFromConflict.Specifications,
            Price = freshDataFromConflict.Price,
            StockQuantity = freshDataFromConflict.StockQuantity, // актуальні 4 шт.
            Version = freshDataFromConflict.Version, // актуальна версія 2
            IsActive = true,
            ImagePath = freshDataFromConflict.ImagePath
        });
        Assert.Equal(HttpStatusCode.OK, confirmedSaveRes.StatusCode);
        var updatedProd = (await confirmedSaveRes.Content.ReadFromJsonAsync<ProductDto>(JsonOpts))!;
        Assert.Equal(4, updatedProd.StockQuantity);
        Assert.Equal(3, updatedProd.Version);
    }

    [Fact(DisplayName = "P3. Конкурентні запити оплати та скасування замовлення: кінцевий стан ніколи не стає одночасно «Скасоване» й «Оплачено»")]
    public async Task CheckP3_ConcurrentPaymentAndCancellationNeverProduceCancelledAndPaidState()
    {
        var adminAuth = await LoginAsync("admin@papir.local", "AdminDemo!2026");
        using var adminClient1 = CreateClientWithToken(adminAuth.Token);
        using var adminClient2 = CreateClientWithToken(adminAuth.Token);

        var buyerAuth = await RegisterFreshBuyerAsync("pay_cancel_race_buyer");
        using var buyerClient = CreateClientWithToken(buyerAuth.Token);

        // Перевіряємо в обох порядках перекриття (1: спочатку оплата, потім скасування; 2: спочатку скасування, потім оплата)
        for (int round = 1; round <= 2; round++)
        {
            await buyerClient.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto { ProductId = 1, Quantity = 1 });
            var cart = await buyerClient.GetFromJsonAsync<CartResponseDto>("/api/cart", JsonOpts);

            var createOrdRes = await buyerClient.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
            {
                RecipientFirstName = "Олена",
                RecipientLastName = "Коваль",
                RecipientPhone = "+380501112233",
                RecipientEmail = buyerAuth.User.Email,
                CartSnapshotToken = cart!.CartSnapshotToken,
                ExpectedTotalAmount = cart.TotalAmount,
                IdempotencyKey = $"idemp-pay-cancel-race-{round}"
            });
            var ord = (await createOrdRes.Content.ReadFromJsonAsync<OrderDto>(JsonOpts))!;

            // Переводимо у «В обробці»
            (await adminClient1.PatchAsJsonAsync($"/api/admin/orders/{ord.Id}/status", new UpdateOrderStatusRequestDto
            {
                NewStatus = OrderStatus.Processing
            })).EnsureSuccessStatusCode();

            var payTask = adminClient1.PatchAsync($"/api/admin/orders/{ord.Id}/payment", null);
            var cancelTask = adminClient2.PatchAsJsonAsync($"/api/admin/orders/{ord.Id}/status", new UpdateOrderStatusRequestDto
            {
                NewStatus = OrderStatus.Cancelled
            });

            var results = await Task.WhenAll(payTask, cancelTask);

            // Рівно один запит має завершитися 200 OK, а інший — 409 Conflict
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

            var finalOrder = await adminClient1.GetFromJsonAsync<OrderDto>($"/api/admin/orders/{ord.Id}", JsonOpts);
            Assert.NotNull(finalOrder);

            // Кінцевий стан ніколи не може бути одночасно Cancelled і IsPaid == true
            Assert.False(finalOrder!.Status == OrderStatus.Cancelled && finalOrder.IsPaid);
        }
    }

    [Fact(DisplayName = "14. Конкуренція двох покупців за останню одиницю товару в реальній PostgreSQL не дає від’ємного залишку")]
    public async Task Check14_ConcurrentCheckoutForLastUnitPreventsOverselling()
    {
        var adminAuth = await LoginAsync("admin@papir.local", "AdminDemo!2026");
        var buyer1Auth = await RegisterFreshBuyerAsync("last_unit_b1");
        var buyer2Auth = await RegisterFreshBuyerAsync("last_unit_b2");

        using var adminClient = CreateClientWithToken(adminAuth.Token);
        using var buyer1Client = CreateClientWithToken(buyer1Auth.Token);
        using var buyer2Client = CreateClientWithToken(buyer2Auth.Token);

        var prodRes = await adminClient.PostAsJsonAsync("/api/admin/products", new SaveProductRequestDto
        {
            CategoryId = 1,
            Name = "Лімітована ручка 1 шт. для тесту конкуренції",
            Description = "Ексклюзивна позиція в єдиному екземплярі",
            Specifications = "Кількість: 1 шт.",
            Price = 150.00m,
            StockQuantity = 1,
            IsActive = true
        });
        var lastItem = (await prodRes.Content.ReadFromJsonAsync<ProductDto>(JsonOpts))!;

        var cart1Res = await buyer1Client.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto { ProductId = lastItem.Id, Quantity = 1 });
        var cart1 = (await cart1Res.Content.ReadFromJsonAsync<CartResponseDto>(JsonOpts))!;

        var cart2Res = await buyer2Client.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto { ProductId = lastItem.Id, Quantity = 1 });
        var cart2 = (await cart2Res.Content.ReadFromJsonAsync<CartResponseDto>(JsonOpts))!;

        var task1 = buyer1Client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            RecipientFirstName = "Олена",
            RecipientLastName = "Коваль",
            RecipientPhone = "+380501112233",
            RecipientEmail = buyer1Auth.User.Email,
            CartSnapshotToken = cart1.CartSnapshotToken,
            ExpectedTotalAmount = 150.00m,
            IdempotencyKey = "idemp-last-unit-b1"
        });

        var task2 = buyer2Client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            RecipientFirstName = "Андрій",
            RecipientLastName = "Бондаренко",
            RecipientPhone = "+380672223344",
            RecipientEmail = buyer2Auth.User.Email,
            CartSnapshotToken = cart2.CartSnapshotToken,
            ExpectedTotalAmount = 150.00m,
            IdempotencyKey = "idemp-last-unit-b2"
        });

        var responses = await Task.WhenAll(task1, task2);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var checkProd = await adminClient.GetFromJsonAsync<List<ProductDto>>($"/api/admin/products?search={Uri.EscapeDataString(lastItem.Name)}", JsonOpts);
        Assert.NotNull(checkProd);
        Assert.Equal(0, checkProd![0].StockQuantity);
    }

    [Fact(DisplayName = "15. Скасування замовлення повертає залишок рівно один раз, зокрема при одночасних запитах")]
    public async Task Check15_ConcurrentOrderCancellationRestoresStockExactlyOnce()
    {
        var adminAuth = await LoginAsync("admin@papir.local", "AdminDemo!2026");
        using var adminClient1 = CreateClientWithToken(adminAuth.Token);
        using var adminClient2 = CreateClientWithToken(adminAuth.Token);

        var prodRes = await adminClient1.PostAsJsonAsync("/api/admin/products", new SaveProductRequestDto
        {
            CategoryId = 1,
            Name = "Товар для перевірки одночасного скасування",
            Description = "Тест повернення залишків",
            Specifications = "Тест: 10 шт.",
            Price = 80.00m,
            StockQuantity = 10,
            IsActive = true
        });
        var prod = (await prodRes.Content.ReadFromJsonAsync<ProductDto>(JsonOpts))!;

        var buyer = await RegisterFreshBuyerAsync("cancel_test_buyer");
        using var buyerClient = CreateClientWithToken(buyer.Token);

        var cartRes = await buyerClient.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto { ProductId = prod.Id, Quantity = 3 });
        var cart = (await cartRes.Content.ReadFromJsonAsync<CartResponseDto>(JsonOpts))!;

        var ordRes = await buyerClient.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            RecipientFirstName = "Ігор",
            RecipientLastName = "Савченко",
            RecipientPhone = "+380503334455",
            RecipientEmail = buyer.User.Email,
            CartSnapshotToken = cart.CartSnapshotToken,
            ExpectedTotalAmount = 240.00m,
            IdempotencyKey = "idemp-cancel-once-test"
        });
        var order = (await ordRes.Content.ReadFromJsonAsync<OrderDto>(JsonOpts))!;

        var prodAfterOrder = await buyerClient.GetFromJsonAsync<ProductDto>($"/api/products/{prod.Id}", JsonOpts);
        Assert.Equal(7, prodAfterOrder!.StockQuantity);

        var cancelTask1 = adminClient1.PatchAsJsonAsync($"/api/admin/orders/{order.Id}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Cancelled
        });
        var cancelTask2 = adminClient2.PatchAsJsonAsync($"/api/admin/orders/{order.Id}/status", new UpdateOrderStatusRequestDto
        {
            NewStatus = OrderStatus.Cancelled
        });

        var cancelResults = await Task.WhenAll(cancelTask1, cancelTask2);

        Assert.Equal(1, cancelResults.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, cancelResults.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var prodAfterCancel = await buyerClient.GetFromJsonAsync<ProductDto>($"/api/products/{prod.Id}", JsonOpts);
        Assert.Equal(10, prodAfterCancel!.StockQuantity);
    }

    [Fact(DisplayName = "12A. Перевірка валідації недостатнього залишку перед записом та перевірка сигнатури растрових зображень")]
    public async Task Check12A_PreWriteStockValidationAndSafeImageUpload()
    {
        var adminAuth = await LoginAsync("admin@papir.local", "AdminDemo!2026");
        using var adminClient = CreateClientWithToken(adminAuth.Token);

        var prodARes = await adminClient.PostAsJsonAsync("/api/admin/products", new SaveProductRequestDto
        {
            CategoryId = 1,
            Name = "Товар А (валідація перед записом)",
            Description = "Опис А",
            Specifications = "Тест: А",
            Price = 50.00m,
            StockQuantity = 5,
            IsActive = true
        });
        var prodA = (await prodARes.Content.ReadFromJsonAsync<ProductDto>(JsonOpts))!;

        var prodBRes = await adminClient.PostAsJsonAsync("/api/admin/products", new SaveProductRequestDto
        {
            CategoryId = 1,
            Name = "Товар Б (валідація перед записом)",
            Description = "Опис Б",
            Specifications = "Тест: Б",
            Price = 60.00m,
            StockQuantity = 2,
            IsActive = true
        });
        var prodB = (await prodBRes.Content.ReadFromJsonAsync<ProductDto>(JsonOpts))!;

        var buyer = await RegisterFreshBuyerAsync("prewrite_val_buyer");
        using var buyerClient = CreateClientWithToken(buyer.Token);

        await buyerClient.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto { ProductId = prodA.Id, Quantity = 2 });
        var cartRes = await buyerClient.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto { ProductId = prodB.Id, Quantity = 2 });
        var cart = (await cartRes.Content.ReadFromJsonAsync<CartResponseDto>(JsonOpts))!;

        // Адміністратор зменшує залишок Товару Б до 1 шт.
        await adminClient.PutAsJsonAsync($"/api/admin/products/{prodB.Id}", new SaveProductRequestDto
        {
            CategoryId = prodB.CategoryId,
            Name = prodB.Name,
            Description = prodB.Description,
            Specifications = prodB.Specifications,
            Price = prodB.Price,
            StockQuantity = 1,
            Version = prodB.Version,
            IsActive = true,
            ImagePath = prodB.ImagePath
        });

        var failedOrderRes = await buyerClient.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            RecipientFirstName = "Наталія",
            RecipientLastName = "Лисенко",
            RecipientPhone = "+380507778899",
            RecipientEmail = buyer.User.Email,
            CartSnapshotToken = cart.CartSnapshotToken,
            ExpectedTotalAmount = 220.00m,
            IdempotencyKey = "idemp-prewrite-stock-check"
        });
        Assert.Equal(HttpStatusCode.Conflict, failedOrderRes.StatusCode);

        // Перевірка безпечного завантаження зображень
        using var fakeForm = new MultipartFormDataContent();
        var fakeBytes = Encoding.UTF8.GetBytes("This is not a valid PNG file header");
        var fakeContent = new ByteArrayContent(fakeBytes);
        fakeContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        fakeForm.Add(fakeContent, "file", "../../evil.png");

        var fakeUploadRes = await adminClient.PostAsync("/api/admin/products/upload-image", fakeForm);
        Assert.Equal(HttpStatusCode.BadRequest, fakeUploadRes.StatusCode);

        byte[] validPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        using var validForm = new MultipartFormDataContent();
        var validContent = new ByteArrayContent(validPng);
        validContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        validForm.Add(validContent, "file", "../traversal_attempt.png");

        var validUploadRes = await adminClient.PostAsync("/api/admin/products/upload-image", validForm);
        Assert.Equal(HttpStatusCode.Created, validUploadRes.StatusCode);
        var uploadJson = await validUploadRes.Content.ReadFromJsonAsync<JsonElement>();
        var savedPath = uploadJson.GetProperty("imagePath").GetString()!;
        Assert.StartsWith("/uploads/products/prod_", savedPath);
        Assert.EndsWith(".png", savedPath);
    }

    [Fact(DisplayName = "12B (P5). Справжня перевірка відкату транзакції після фактичного INSERT замовлення, UPDATE залишків товарів та INSERT order_items")]
    public async Task Check12B_TrueTransactionRollbackAfterDatabaseWritesMidTransaction()
    {
        var adminAuth = await LoginAsync("admin@papir.local", "AdminDemo!2026");
        using var adminClient = CreateClientWithToken(adminAuth.Token);

        // Створюємо 2 товари з залишками 10 та 8 шт.
        var prod1Res = await adminClient.PostAsJsonAsync("/api/admin/products", new SaveProductRequestDto
        {
            CategoryId = 1,
            Name = "Товар 1 для справжнього тесту відкату транзакції",
            Description = "Опис 1",
            Specifications = "Тест: Rollback 1",
            Price = 100.00m,
            StockQuantity = 10,
            IsActive = true
        });
        var prod1 = (await prod1Res.Content.ReadFromJsonAsync<ProductDto>(JsonOpts))!;

        var prod2Res = await adminClient.PostAsJsonAsync("/api/admin/products", new SaveProductRequestDto
        {
            CategoryId = 1,
            Name = "Товар 2 для справжнього тесту відкату транзакції",
            Description = "Опис 2",
            Specifications = "Тест: Rollback 2",
            Price = 150.00m,
            StockQuantity = 8,
            IsActive = true
        });
        var prod2 = (await prod2Res.Content.ReadFromJsonAsync<ProductDto>(JsonOpts))!;

        var buyer = await RegisterFreshBuyerAsync("true_rollback_buyer");
        using var buyerClient = CreateClientWithToken(buyer.Token);

        await buyerClient.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto { ProductId = prod1.Id, Quantity = 3 });
        var cartRes = await buyerClient.PostAsJsonAsync("/api/cart/items", new AddToCartRequestDto { ProductId = prod2.Id, Quantity = 2 });
        var cart = (await cartRes.Content.ReadFromJsonAsync<CartResponseDto>(JsonOpts))!;

        bool orderInsertedInTransaction = false;
        int orderItemsInsertedInTransaction = 0;
        int stockUpdateCountInDb = 0;
        int cartItemsRemainingBeforeRollback = -1;
        int p1StockInsideTxBeforeRollback = -1;

        var cmdTracker = new ControlledDbCommandInterceptor(
            onAfterExecuteAsync: cmd =>
            {
                if (cmd.CommandText.Contains("UPDATE products SET stock_quantity", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref stockUpdateCountInDb);
                }
                return Task.CompletedTask;
            });

        // Перехоплювач транзакцій безпосередньо перед COMMIT (але всередині відкритої транзакції!)
        // запитує стан таблиць у самій транзакції (щоб переконатися, що INSERT orders, INSERT order_items, UPDATE products та DELETE cart_items вже виконані у БД),
        // а потім кидає виняток для примусового ROLLBACK.
        var commitFailureInterceptor = new ControlledTransactionInterceptor(
            onCommittingAsync: async tx =>
            {
                await using (var checkCmd = tx.Connection!.CreateCommand())
                {
                    checkCmd.Transaction = tx;
                    checkCmd.CommandText = $@"
                        SELECT
                            (SELECT COUNT(*) FROM orders WHERE user_id = {buyer.User.Id}),
                            (SELECT COUNT(*) FROM order_items WHERE product_id IN ({prod1.Id}, {prod2.Id})),
                            (SELECT COUNT(*) FROM cart_items WHERE user_id = {buyer.User.Id}),
                            (SELECT stock_quantity FROM products WHERE id = {prod1.Id});";
                    await using var reader = await checkCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        orderInsertedInTransaction = reader.GetInt64(0) == 1;
                        orderItemsInsertedInTransaction = (int)reader.GetInt64(1);
                        cartItemsRemainingBeforeRollback = (int)reader.GetInt64(2);
                        p1StockInsideTxBeforeRollback = reader.GetInt32(3);
                    }
                }

                throw new InvalidOperationException("SIMULATED_MID_TRANSACTION_FAILURE_AFTER_ORDER_AND_STOCK_WRITES");
            });

        await using (var failingDbContext = _fixture.CreateIsolatedDbContext(cmdTracker, commitFailureInterceptor))
        {
            var orderService = new OrderService(failingDbContext, Options.Create(_fixture.PickupConfig));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orderService.CreateOrderAsync(buyer.User.Id, new CreateOrderRequestDto
                {
                    RecipientFirstName = "Оксана",
                    RecipientLastName = "Бойко",
                    RecipientPhone = "+380504445566",
                    RecipientEmail = buyer.User.Email,
                    CartSnapshotToken = cart.CartSnapshotToken,
                    ExpectedTotalAmount = cart.TotalAmount,
                    IdempotencyKey = "idemp-true-rollback-after-writes"
                }));

            Assert.Equal("SIMULATED_MID_TRANSACTION_FAILURE_AFTER_ORDER_AND_STOCK_WRITES", ex.Message);
        }

        // Переконуємося, що всередині транзакції ДО моменту збою в БД вже були записані замовлення, 2 позиції order_items, списані залишки (10 - 3 = 7) і очищений кошик (0)
        Assert.True(orderInsertedInTransaction, "INSERT INTO orders мав фактично виконатися в БД до штучного збою.");
        Assert.Equal(2, orderItemsInsertedInTransaction);
        Assert.Equal(2, stockUpdateCountInDb);
        Assert.Equal(0, cartItemsRemainingBeforeRollback);
        Assert.Equal(7, p1StockInsideTxBeforeRollback);

        // Тепер перевіряємо через новий незалежний DbContext, що транзакція повністю відкотилася:
        // 1) немає жодного замовлення та жодної позиції order_items для цього користувача;
        // 2) залишки обох товарів та їхні версії незмінні (10 і 8 шт., версія 1);
        // 3) кошик користувача повністю збережений (2 позиції: 3 шт. та 2 шт.).
        await using (var verifyDb = _fixture.CreateIsolatedDbContext())
        {
            var ordersInDb = await verifyDb.Orders.AsNoTracking().Where(o => o.UserId == buyer.User.Id).ToListAsync();
            Assert.Empty(ordersInDb);

            var orderItemsInDb = await verifyDb.OrderItems.AsNoTracking()
                .Where(oi => oi.ProductId == prod1.Id || oi.ProductId == prod2.Id)
                .ToListAsync();
            Assert.Empty(orderItemsInDb);

            var p1After = await verifyDb.Products.AsNoTracking().SingleAsync(p => p.Id == prod1.Id);
            var p2After = await verifyDb.Products.AsNoTracking().SingleAsync(p => p.Id == prod2.Id);
            Assert.Equal(10, p1After.StockQuantity);
            Assert.Equal(1, p1After.Version);
            Assert.Equal(8, p2After.StockQuantity);
            Assert.Equal(1, p2After.Version);

            var cartItemsAfter = await verifyDb.CartItems.AsNoTracking()
                .Where(ci => ci.UserId == buyer.User.Id)
                .OrderBy(ci => ci.ProductId)
                .ToListAsync();
            Assert.Equal(2, cartItemsAfter.Count);
            Assert.Equal(3, cartItemsAfter.Single(ci => ci.ProductId == prod1.Id).Quantity);
            Assert.Equal(2, cartItemsAfter.Single(ci => ci.ProductId == prod2.Id).Quantity);
        }
    }

    [Fact(DisplayName = "P6. Повторне наповнення БД (SeedDemoDataIfDevelopmentAsync) зіставляє товари за незмінним SeedKey: перейменування демонстраційного товару та зміна ціни/фото не створюють дублікатів і не перезаписуються")]
    public async Task CheckP6_ReseedingAfterRenamingDemoProductDoesNotCreateDuplicateAndPreservesChanges()
    {
        var adminAuth = await LoginAsync("admin@papir.local", "AdminDemo!2026");
        using var adminClient = CreateClientWithToken(adminAuth.Token);

        // 1. Перевіряємо, що «Тестовий блокнот А6» НЕ створюється при початковому наповненні нової БД
        var allInitial = await adminClient.GetFromJsonAsync<List<ProductDto>>("/api/admin/products", JsonOpts);
        Assert.NotNull(allInitial);
        Assert.DoesNotContain(allInitial!, p => p.Name.Equals("Тестовий блокнот А6", StringComparison.OrdinalIgnoreCase));

        // Перевіряємо, що всі 23 демонстраційні товари мають остаточні фото з /uploads/products/ і файли фізично існують на диску
        var webRoot = Path.Combine(
            Path.GetDirectoryName(_fixture.GetType().Assembly.Location)!,
            "..", "..", "..", "..", "backend", "wwwroot");
        webRoot = Path.GetFullPath(webRoot);

        const string originalPlannerName = "Планер тижневий недатований А5, графітовий";
        var demoSeedNames = new[]
        {
            "Блокнот А5, 80 аркушів, оливковий",
            "Блокнот А5, 96 аркушів, пудровий",
            "Зошит А5, 48 аркушів, теракотовий",
            "Блокнот А4 на спіралі, 120 аркушів, бургунді",
            originalPlannerName,
            "Набір кулькових ручок, 3 шт.",
            "Ручка гелева чорна 0.5 мм FineLine",
            "Набір текстових маркерів пастельних, 4 шт.",
            "Олівець механічний 0.5 мм зі змінними грифелями HB",
            "Набір чорнографітних олівців HB–2B, 6 шт.",
            "Папка на гумках А4, шавлієва",
            "Папка для документів А4, пісочна",
            "Папка-реєстратор А4 арочна 50 мм, бордова",
            "Органайзер для документів А4 на 12 відділень",
            "Органайзер настільний металевий, графіт",
            "Лоток вертикальний для паперів А4, молочний",
            "Степлер металевий №24/6 на 25 аркушів + скоби",
            "Набір біндерів та скріпок у боксі, 80 шт.",
            "Папір офісний А4 80 г/м², 500 аркушів, клас А",
            "Блок паперових стікерів із клейким шаром 75×75 мм, 400 арк.",
            "Скетчбук для акварелі А5, 200 г/м² (немає в наявності)",
            "Лінійка металева 30 см, матова сталь (прихована пропозиція)",
            "Щоденник датований А5 «папір. Бургунді 2026»"
        };

        foreach (var expectedName in demoSeedNames)
        {
            var found = allInitial.FirstOrDefault(p => p.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(found);
            Assert.StartsWith("/uploads/products/prod_", found!.ImagePath);
            var diskPath = Path.Combine(webRoot, found.ImagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(diskPath), $"Файл зображення має існувати на диску: {diskPath}");
        }

        var countBeforeRename = allInitial.Count;

        // 2. Адміністратор вручну ПЕРЕЙМЕНОВУЄ демонстраційний товар «Планер тижневий недатований А5, графітовий», а також змінює ціну, опис і фото
        var planner = allInitial.First(p => p.Name.Equals(originalPlannerName, StringComparison.OrdinalIgnoreCase));
        const string renamedPlannerName = "Планер тижневий недатований А5, антрацитовий (оновлена назва)";
        const string customManualImagePath = "/uploads/products/prod_manual_custom_test.png";
        const decimal customManualPrice = 425.00m;
        const string customManualDescription = "Вручну змінений опис і назва адміністратором, які не повинні перезаписуватися чи дублюватися.";

        var updateRes = await adminClient.PutAsJsonAsync($"/api/admin/products/{planner.Id}", new SaveProductRequestDto
        {
            CategoryId = planner.CategoryId,
            Name = renamedPlannerName,
            Description = customManualDescription,
            Specifications = planner.Specifications,
            Price = customManualPrice,
            StockQuantity = planner.StockQuantity,
            Version = planner.Version,
            IsActive = planner.IsActive,
            ImagePath = customManualImagePath
        });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        // 3. Викликаємо повторно DatabaseInitializer.SeedDemoDataIfDevelopmentAsync(forceSeed: true)
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
            await initializer.SeedDemoDataIfDevelopmentAsync(forceSeed: true);
        }

        // 4. Перевіряємо, що:
        //    - загальна кількість товарів не збільшилася (жодного дубліката не створено);
        //    - товар під початковою назвою не з'явився повторно;
        //    - перейменований товар зберіг свій ID, нову назву, нову ціну, новий опис та нове фото.
        var allAfterReseed = await adminClient.GetFromJsonAsync<List<ProductDto>>("/api/admin/products", JsonOpts);
        Assert.NotNull(allAfterReseed);
        Assert.Equal(countBeforeRename, allAfterReseed!.Count);
        Assert.DoesNotContain(allAfterReseed, p => p.Name.Equals(originalPlannerName, StringComparison.OrdinalIgnoreCase));

        var renamedAfter = allAfterReseed.Where(p => p.Id == planner.Id).ToList();
        Assert.Single(renamedAfter);
        Assert.Equal(renamedPlannerName, renamedAfter[0].Name);
        Assert.Equal(customManualPrice, renamedAfter[0].Price);
        Assert.Equal(customManualDescription, renamedAfter[0].Description);
        Assert.Equal(customManualImagePath, renamedAfter[0].ImagePath);
    }
}
