namespace AtoZClinical.Core.Entities;

public class DoctorSurgery : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int SurgeryNo { get; set; }
    public DateTime RecordDate { get; set; } = DateTime.Today;
    public DateTime? SurgeryDate { get; set; }
    public TimeSpan? SurgeryTime { get; set; }
    public Guid? PatientRecordId { get; set; }
    public string? PatientName { get; set; }
    public string? PatientBarcode { get; set; }
    public int? Age { get; set; }
    public string? City { get; set; }
    public string? NationalId { get; set; }
    public string? Phone { get; set; }
    public string? MotherName { get; set; }
    public Guid? DoctorRecordId { get; set; }
    public string? DoctorName { get; set; }
    public string? Specialty { get; set; }
    public string? TypeOfSurgery { get; set; }
    public string? Classify { get; set; }
    public string? SurgeryName { get; set; }
    public decimal InitialAmount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
}

public class RoomBooking : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int BookingNo { get; set; }
    public DateTime DateBook { get; set; } = DateTime.Today;
    public Guid? PatientRecordId { get; set; }
    public Guid? DoctorSurgeryId { get; set; }
    public string? PatientName { get; set; }
    public string? PatientBarcode { get; set; }
    public int? Age { get; set; }
    public string? City { get; set; }
    public string? NationalId { get; set; }
    public string? Phone { get; set; }
    public string? MotherName { get; set; }
    public string? DoctorName { get; set; }
    public string? Specialty { get; set; }
    public string? TypeOfSurgery { get; set; }
    public string? Classify { get; set; }
    public string? SurgeryName { get; set; }
    public int? RoomNumber { get; set; }
    public DateTime? EnterDate { get; set; }
    public DateTime? ExitDate { get; set; }
    public TimeSpan? EnterTime { get; set; }
    public TimeSpan? ExitTime { get; set; }
    public int? Days { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
}

public static class WardRoomStatuses
{
    public const string Remaining = "Remaining";
    public const string Empty = "Empty";
    public const string Booked = "Booked";
}

public static class SurgeryTypeOptions
{
    public static readonly string[] All =
    [
        "Minor",
        "Moderate",
        "Major",
        "Super Major",
        "Skill",
        "Special",
        "Comprehensive",
        "Other"
    ];
}

public class WardRoom : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int RoomNo { get; set; }
    public string Status { get; set; } = WardRoomStatuses.Remaining;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
}
