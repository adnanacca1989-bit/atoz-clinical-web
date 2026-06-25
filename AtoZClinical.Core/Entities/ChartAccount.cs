namespace AtoZClinical.Core.Entities;

public class ChartAccount : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int AccountNo { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CategoryType { get; set; } = "Income";
    public string DetailType { get; set; } = "Service/Fee Income";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
}
