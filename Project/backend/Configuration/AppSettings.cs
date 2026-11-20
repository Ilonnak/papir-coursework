namespace Backend.Configuration;

public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 240;
}

public class PickupPointSettings
{
    public string Title { get; set; } = "Навчальний демонстраційний пункт самовивозу «папір.»";
    public string Address { get; set; } = string.Empty;
    public string Schedule { get; set; } = string.Empty;
    public string PaymentNote { get; set; } = string.Empty;
}

public class DemoSeedSettings
{
    public bool Enabled { get; set; } = true;
    public DemoAccountConfig AdminAccount { get; set; } = new();
    public List<DemoAccountConfig> CustomerAccounts { get; set; } = new();
}

public class DemoAccountConfig
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
}
