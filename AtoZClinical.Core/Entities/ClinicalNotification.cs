namespace AtoZClinical.Core.Entities;

public class ClinicalNotification : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    /// <summary>Target department role: Laboratory, Radiology, Pharmacy (also Lab, Cashier).</summary>
    public string TargetRole { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
}
