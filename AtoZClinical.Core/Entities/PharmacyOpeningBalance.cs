namespace AtoZClinical.Core.Entities;

public class PharmacyOpeningBalance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int BalanceNo { get; set; }
    public DateTime BalanceDate { get; set; } = DateTime.Today;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
    public ICollection<PharmacyOpeningBalanceLine> Lines { get; set; } = [];
}

public class PharmacyOpeningBalanceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PharmacyOpeningBalanceId { get; set; }
    public int LineNo { get; set; }
    public string? Barcode { get; set; }
    public string? MedicineCode { get; set; }
    public string? MedicineName { get; set; }
    public string? Dosage { get; set; }
    public string? Uom { get; set; }
    public int Qty { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Total { get; set; }

    public PharmacyOpeningBalance PharmacyOpeningBalance { get; set; } = null!;
}
