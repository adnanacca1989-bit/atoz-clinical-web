namespace AtoZClinical.Core.Entities;

public class PharmacyInventoryMovement : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid PharmacyItemId { get; set; }
    public DateTime MovementDate { get; set; } = DateTime.Today;
    public string MovementType { get; set; } = string.Empty;
    public string ReferenceType { get; set; } = string.Empty;
    public Guid ReferenceId { get; set; }
    public int? ReferenceNo { get; set; }
    public int LineNo { get; set; }
    public string? Barcode { get; set; }
    public string? MedicineCode { get; set; }
    public string? MedicineName { get; set; }
    public int QtyIn { get; set; }
    public int QtyOut { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalValue { get; set; }
    public int BalanceQtyAfter { get; set; }
    public decimal MovingAvgCostAfter { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
    public PharmacyItem PharmacyItem { get; set; } = null!;
}
