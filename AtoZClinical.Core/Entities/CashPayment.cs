namespace AtoZClinical.Core.Entities;

public class CashPayment : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int PaymentNo { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public string? PayeeName { get; set; }
    public string? PatientId { get; set; }
    public string? DoctorName { get; set; }
    public string? PayeeType { get; set; }
    public string? ChartAccountName { get; set; }
    public decimal Amount { get; set; }
    public string WrittenAmount { get; set; } = "Zero";
    public string PaymentMethod { get; set; } = "Cash";
    public string? ReferenceNo { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
}
