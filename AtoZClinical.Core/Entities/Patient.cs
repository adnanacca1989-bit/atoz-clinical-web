namespace AtoZClinical.Core.Entities;

public class Patient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public string PatientNo { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? BloodGroup { get; set; }
    public string? Allergies { get; set; }
    public string? NationalId { get; set; }
    public string? EmergencyContact { get; set; }
    public string? DoctorName { get; set; }
    public string? Specialty { get; set; }
    public string? AppointmentId { get; set; }
    public string? VisitNumber { get; set; }
    public DateTime? AppointmentDate { get; set; }
    public TimeSpan? AppointmentTime { get; set; }
    public string Status { get; set; } = "Active";
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public Clinic Clinic { get; set; } = null!;
    public ICollection<Appointment> Appointments { get; set; } = [];

    public string FullName => string.IsNullOrWhiteSpace(LastName)
        ? FirstName.Trim()
        : $"{FirstName} {LastName}".Trim();

    public int? AgeYears => DateOfBirth.HasValue
        ? Math.Max(0, (int)((DateTime.Today - DateOfBirth.Value.Date).TotalDays / 365.25))
        : null;
}
