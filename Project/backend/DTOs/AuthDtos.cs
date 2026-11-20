using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

public class RegisterRequestDto
{
    [Required(ErrorMessage = "Вкажіть електронну пошту.")]
    [EmailAddress(ErrorMessage = "Некоректний формат електронної пошти.")]
    [MaxLength(255, ErrorMessage = "Email не може перевищувати 255 символів.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть пароль.")]
    [MinLength(6, ErrorMessage = "Пароль повинен містити щонайменше 6 символів.")]
    [MaxLength(100, ErrorMessage = "Пароль не може перевищувати 100 символів.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть ім’я.")]
    [MaxLength(100, ErrorMessage = "Ім’я не може перевищувати 100 символів.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть прізвище.")]
    [MaxLength(100, ErrorMessage = "Прізвище не може перевищувати 100 символів.")]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(100, ErrorMessage = "По батькові не може перевищувати 100 символів.")]
    public string? MiddleName { get; set; }
}

public class LoginRequestDto
{
    [Required(ErrorMessage = "Вкажіть електронну пошту.")]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть пароль.")]
    [MaxLength(100)]
    public string Password { get; set; } = string.Empty;
}

public class UserProfileDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public bool IsAdmin { get; set; }
    public string Role => IsAdmin ? "Admin" : "Buyer";
    public DateTime CreatedAt { get; set; }
}

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public UserProfileDto User { get; set; } = new();
}
