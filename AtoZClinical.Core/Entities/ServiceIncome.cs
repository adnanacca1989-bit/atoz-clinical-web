namespace AtoZClinical.Core.Entities;

public class ServiceIncome : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int ServiceNo { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AccountName { get; set; } = "Clinical Revenue";
    public decimal Fee { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
}
