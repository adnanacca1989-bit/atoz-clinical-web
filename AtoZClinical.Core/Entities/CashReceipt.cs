namespace AtoZClinical.Core.Entities;

public class CashReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int ReceiptNo { get; set; }
    public DateTime ReceiptDate { get; set; } = DateTime.Today;
    public string? PatientSearch { get; set; }
    public string? PatientId { get; set; }
    public string? PatientName { get; set; }
    public DateTime? AppointmentDate { get; set; }
    public TimeSpan? AppointmentTime { get; set; }
    public string? DoctorName { get; set; }
    public decimal BalanceDue { get; set; }
    public string? BalanceStatus { get; set; }
    public decimal? EndingBalance { get; set; }
    public decimal Amount { get; set; }
    public string WrittenAmount { get; set; } = "Zero";
    public string PaymentMethod { get; set; } = "Cash";
    public string? ReferenceNo { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
}
