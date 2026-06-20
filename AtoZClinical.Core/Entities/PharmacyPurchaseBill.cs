namespace AtoZClinical.Core.Entities;

public class PharmacyPurchaseBill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int PurchaseNo { get; set; }
    public DateTime PurchaseDate { get; set; } = DateTime.Today;
    public string? SupplierName { get; set; }
    public string? SupplierPhone { get; set; }
    public string? SupplierInvoiceNo { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal NetAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal BalanceDue { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public string? PaymentStatus { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
    public ICollection<PharmacyPurchaseBillLine> Lines { get; set; } = [];
}

public class PharmacyPurchaseBillLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PharmacyPurchaseBillId { get; set; }
    public int LineNo { get; set; }
    public string? Barcode { get; set; }
    public string? MedicineCode { get; set; }
    public string? MedicineName { get; set; }
    public string? Dosage { get; set; }
    public string? Uom { get; set; }
    public int Qty { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }

    public PharmacyPurchaseBill PharmacyPurchaseBill { get; set; } = null!;
}
