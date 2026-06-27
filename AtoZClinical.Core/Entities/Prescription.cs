namespace AtoZClinical.Core.Entities;

public class Prescription : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int PrescriptionNo { get; set; }
    public string? PatientName { get; set; }
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? DoctorName { get; set; }
    public string? Specialty { get; set; }
    public DateTime DatePrescription { get; set; } = DateTime.Today;
    public string? DiseaseName { get; set; }
    public string? ChronicDiseasesJson { get; set; }
    public string? DiagnosisText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
    public ICollection<PrescriptionLine> Lines { get; set; } = [];
}

public class PrescriptionLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PrescriptionId { get; set; }
    public int LineNo { get; set; }
    public Guid? PharmacyItemId { get; set; }
    public string? MedicineName { get; set; }
    public string? MedicationForm { get; set; }
    public string? Dose { get; set; }
    public string? Unit { get; set; }
    public string? Frequency { get; set; }
    public string? Duration { get; set; }
    public string? Instruction { get; set; }

    public Prescription Prescription { get; set; } = null!;
}
