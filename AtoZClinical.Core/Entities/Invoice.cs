namespace AtoZClinical.Core.Entities;

public class Invoice : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int InvoiceNo { get; set; }
    public DateTime InvoiceDate { get; set; } = DateTime.Today;
    public string? PatientName { get; set; }
    public string? PatientId { get; set; }
    public Guid? PatientRecordId { get; set; }
    public Guid? DoctorRecordId { get; set; }
    public string? Phone { get; set; }
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? City { get; set; }
    public string? DoctorName { get; set; }
    public string? Specialty { get; set; }
    public decimal SubTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal BalanceDue { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public string? PaymentStatus { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
    public ICollection<InvoiceLine> Lines { get; set; } = [];
}

public class InvoiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public int LineNo { get; set; }
    public int? ServiceNo { get; set; }
    public string? ServiceName { get; set; }
    public string? AccountName { get; set; }
    public int Qty { get; set; } = 1;
    public decimal UnitFee { get; set; }
    public decimal LineTotal { get; set; }

    public Invoice Invoice { get; set; } = null!;
}
