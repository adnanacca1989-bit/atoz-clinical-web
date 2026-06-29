namespace AtoZClinical.Core.Entities;

/// <summary>Time-limited password reset token for identity users (not clinic-scoped).</summary>
public class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    /// <summary>SHA-256 hash of the token sent by email (never store plaintext).</summary>
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }
    public bool Used { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
