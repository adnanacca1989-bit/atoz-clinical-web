namespace AtoZClinical.Core.Entities;

/// <summary>Platform-wide security events (login, logout, failed auth). Not tenant-filtered.</summary>
public class SecurityAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ClinicId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
