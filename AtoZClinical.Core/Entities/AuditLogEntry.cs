namespace AtoZClinical.Core.Entities;

public class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime DateTime { get; set; } = DateTime.UtcNow;
    public string? UserName { get; set; }
    public string? FormName { get; set; }
    public string? Details { get; set; }

    public Clinic Clinic { get; set; } = null!;
}
