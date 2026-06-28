namespace AtoZClinical.Core.Entities;

public class PharmacyBill : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int BillNo { get; set; }
    public DateTime BillDate { get; set; } = DateTime.Today;
    public int? RequestNo { get; set; }
    public string? PatientName { get; set; }
    public string? PatientId { get; set; }
    public Guid? DoctorRecordId { get; set; }
    public string? DoctorName { get; set; }
    public string? Specialty { get; set; }
    public decimal SubTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal BalanceDue { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public string? PaymentStatus { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
    public ICollection<PharmacyBillLine> Lines { get; set; } = [];
}

public class PharmacyBillLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PharmacyBillId { get; set; }
    public int LineNo { get; set; }
    public string? Barcode { get; set; }
    public string? MedicineCode { get; set; }
    public string? MedicineName { get; set; }
    public string? Dosage { get; set; }
    public string? Uom { get; set; }
    public int Qty { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

    public PharmacyBill PharmacyBill { get; set; } = null!;
}
