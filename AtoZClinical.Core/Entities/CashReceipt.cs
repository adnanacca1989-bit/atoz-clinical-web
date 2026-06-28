namespace AtoZClinical.Core.Entities;

public class CashReceipt : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int ReceiptNo { get; set; }
    public DateTime ReceiptDate { get; set; } = DateTime.Today;
    public string? PatientSearch { get; set; }
    public string? PatientId { get; set; }
    public string? PatientName { get; set; }
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string? Specialty { get; set; }
    public DateTime? AppointmentDate { get; set; }
    public TimeSpan? AppointmentTime { get; set; }
    public Guid? DoctorRecordId { get; set; }
    public string? DoctorName { get; set; }
    public decimal BalanceDue { get; set; }
    public string? BalanceStatus { get; set; }
    public decimal? EndingBalance { get; set; }
    public decimal PatientCredit { get; set; }
    public decimal Amount { get; set; }
    public string WrittenAmount { get; set; } = "Zero";
    public string PaymentMethod { get; set; } = "Cash";
    public string? ChartAccountName { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
}
