namespace AtoZClinical.Core.Entities;

public class PharmacyItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int ItemNo { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string MedicineCode { get; set; } = string.Empty;
    public string MedicineName { get; set; } = string.Empty;
    public string? Dosage { get; set; }
    public string BaseUom { get; set; } = "Pcs";
    public string? AlternateUom { get; set; }
    public decimal ConversionFactor { get; set; } = 1;
    public decimal DefaultUnitPrice { get; set; }
    public decimal MovingAverageCost { get; set; }
    public int ReorderPoint { get; set; }
    public string? IncomeAccountName { get; set; }
    public string? CostAccountName { get; set; }
    public string? InventoryAccountName { get; set; }
    public bool IsActive { get; set; } = true;
    public int QuantityOnHand { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
    public ICollection<PharmacyInventoryMovement> Movements { get; set; } = [];

    public int ToBaseQuantity(int qty, string? uom)
    {
        if (qty <= 0) return 0;
        if (!string.IsNullOrWhiteSpace(AlternateUom) &&
            string.Equals(uom?.Trim(), AlternateUom, StringComparison.OrdinalIgnoreCase) &&
            ConversionFactor > 0)
            return (int)Math.Round(qty * ConversionFactor, MidpointRounding.AwayFromZero);
        return qty;
    }
}
