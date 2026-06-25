namespace AtoZClinical.Core.Entities;

public class WebhookSubscription : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public string TargetUrl { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    /// <summary>Comma-separated event names, e.g. patient.created,appointment.created</summary>
    public string Events { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
}
