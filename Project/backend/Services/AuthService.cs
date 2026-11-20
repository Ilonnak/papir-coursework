using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Backend.Configuration;
using Backend.Data;
using Backend.DTOs;
using Backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Services;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly JwtSettings _jwtSettings;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(AppDbContext db, IOptions<JwtSettings> jwtOptions)
    {
        _db = db;
        _jwtSettings = jwtOptions.Value;
    }

    public async Task<ServiceResult<AuthResponseDto>> RegisterAsync(RegisterRequestDto dto)
    {
        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return ServiceResult<AuthResponseDto>.Fail("Вкажіть коректну електронну пошту.");
        }

        var exists = await _db.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail);
        if (exists)
        {
            return ServiceResult<AuthResponseDto>.Fail("Користувач із такою електронною поштою вже зареєстрований.", 409, "EMAIL_EXISTS");
        }

        // Публічна реєстрація завжди створює лише покупця (IsAdmin = false)
        var user = new User
        {
            Email = normalizedEmail,
            FirstName = dto.FirstName.Trim(),
            LastName = dto.LastName.Trim(),
            MiddleName = string.IsNullOrWhiteSpace(dto.MiddleName) ? null : dto.MiddleName.Trim(),
            IsAdmin = false,
            CreatedAt = DateTime.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);

        _db.Users.Add(user);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return ServiceResult<AuthResponseDto>.Fail("Користувач із такою електронною поштою вже існує.", 409, "EMAIL_EXISTS");
        }

        var response = CreateAuthResponse(user);
        return ServiceResult<AuthResponseDto>.Ok(response, 201);
    }

    public async Task<ServiceResult<AuthResponseDto>> LoginAsync(LoginRequestDto dto)
    {
        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
        if (user == null)
        {
            return ServiceResult<AuthResponseDto>.Fail("Неправильна електронна пошта або пароль.", 401, "INVALID_CREDENTIALS");
        }

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return ServiceResult<AuthResponseDto>.Fail("Неправильна електронна пошта або пароль.", 401, "INVALID_CREDENTIALS");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);
            await _db.SaveChangesAsync();
        }

        return ServiceResult<AuthResponseDto>.Ok(CreateAuthResponse(user));
    }

    public async Task<ServiceResult<UserProfileDto>> GetCurrentUserAsync(int userId)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return ServiceResult<UserProfileDto>.Fail("Користувача не знайдено.", 401);
        }

        return ServiceResult<UserProfileDto>.Ok(MapUser(user));
    }

    public string HashPassword(User user, string password)
    {
        return _passwordHasher.HashPassword(user, password);
    }

    private AuthResponseDto CreateAuthResponse(User user)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes > 0 ? _jwtSettings.ExpirationMinutes : 240);
        var keyBytes = Encoding.UTF8.GetBytes(_jwtSettings.Key);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.IsAdmin ? "Admin" : "Buyer"),
            new("is_admin", user.IsAdmin ? "true" : "false"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
        var descriptor = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        var token = new JwtSecurityTokenHandler().WriteToken(descriptor);

        return new AuthResponseDto
        {
            Token = token,
            ExpiresAtUtc = expiresAt,
            User = MapUser(user)
        };
    }

    public static UserProfileDto MapUser(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        MiddleName = user.MiddleName,
        IsAdmin = user.IsAdmin,
        CreatedAt = DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc)
    };
}
