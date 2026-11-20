using System.Text;
using System.Text.Json;
using Backend.Configuration;
using Backend.Data;
using Backend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

// Дозволяємо Npgsql коректно працювати з timestamp without time zone та DateTime
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

var backendUrl = builder.Configuration["ServerUrls:Backend"];
if (!string.IsNullOrWhiteSpace(backendUrl))
{
    builder.WebHost.UseUrls(backendUrl);
}

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<PickupPointSettings>(builder.Configuration.GetSection("PickupPoint"));
builder.Services.Configure<DemoSeedSettings>(builder.Configuration.GetSection("DemoSeed"));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured in appsettings.json.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<ImageStorageService>();
builder.Services.AddScoped<DatabaseInitializer>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://127.0.0.1:5173" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalFrontendCors", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
    ?? throw new InvalidOperationException("JWT configuration section is missing in appsettings.json.");

var keyBytes = Encoding.UTF8.GetBytes(jwtSettings.Key);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json; charset=utf-8";
                var msg = context.AuthenticateFailure is SecurityTokenExpiredException
                    ? "Строк дії сесії закінчився. Будь ласка, увійдіть повторно."
                    : "Для виконання цієї дії необхідно увійти до облікового запису.";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = msg, code = "UNAUTHORIZED" }));
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    message = "Недостатньо прав доступу для виконання цієї операції.",
                    code = "FORBIDDEN"
                }));
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(kvp => kvp.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );

            var firstError = errors.Values.SelectMany(x => x).FirstOrDefault()
                             ?? "Перевірте правильність заповнення полів форми.";

            return new BadRequestObjectResult(new
            {
                message = firstError,
                code = "VALIDATION_ERROR",
                errors
            });
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Каталог канцелярських товарів «папір.» API",
        Version = "v1",
        Description = "Навчальний вебзастосунок для курсової роботи: каталог канцелярії, керування пропозиціями та оформлення замовлень."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Введіть JWT токен у форматі: Bearer {token}"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Глобальний обробник помилок без витоку stack trace чи секретів
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        if (exceptionFeature?.Error != null)
        {
            logger.LogError(exceptionFeature.Error, "Необроблена помилка під час виконання HTTP-запиту {Path}", context.Request.Path);
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            message = "Сталася внутрішня помилка сервера. Спробуйте повторити дію пізніше.",
            code = "INTERNAL_SERVER_ERROR"
        }));
    });
});

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "папір. API v1");
});

var webRootPath = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(webRootPath);
app.UseStaticFiles();

app.UseCors("LocalFrontendCors");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "papir-backend" }));
app.MapControllers();

// Підготовка схеми, обов'язкових статусів та опціональне ідемпотентне наповнення для Development / прапорця --seed-demo
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeSchemaAndStatusesAsync();

    var explicitSeed = args.Contains("--seed-demo", StringComparer.OrdinalIgnoreCase);
    await initializer.SeedDemoDataIfDevelopmentAsync(forceSeed: explicitSeed);

    if (args.Contains("--seed-only", StringComparer.OrdinalIgnoreCase))
    {
        return;
    }
}

app.Run();

public partial class Program { }
