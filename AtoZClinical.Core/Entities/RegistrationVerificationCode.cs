namespace AtoZClinical.Core.Entities;

public enum RegistrationVerificationChannel
{
    Email = 0,
    Sms = 1,
    WhatsApp = 2
}

/// <summary>Time-limited 4-digit registration code (hashed at rest).</summary>
public class RegistrationVerificationCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public RegistrationVerificationChannel Channel { get; set; }
    public string Destination { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }
    public bool Used { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
