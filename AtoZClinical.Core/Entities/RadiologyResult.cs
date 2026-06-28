namespace AtoZClinical.Core.Entities;

public class RadiologyResult : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int ResultNo { get; set; }
    public int? RequestNo { get; set; }
    public DateTime ResultDate { get; set; } = DateTime.Today;
    public Guid? PatientRecordId { get; set; }
    public string? PatientName { get; set; }
    public Guid? DoctorRecordId { get; set; }
    public string? DoctorName { get; set; }
    public string? Specialty { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
    public ICollection<RadiologyResultLine> Lines { get; set; } = [];
}

public class RadiologyResultLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RadiologyResultId { get; set; }
    public int LineNo { get; set; }
    public string? TestCode { get; set; }
    public string? TestName { get; set; }
    public string? Category { get; set; }
    public string? Result { get; set; }
    public string? Impression { get; set; }
    public string? Findings { get; set; }

    public RadiologyResult RadiologyResult { get; set; } = null!;
}
