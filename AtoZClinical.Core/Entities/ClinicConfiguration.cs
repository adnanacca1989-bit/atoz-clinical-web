namespace AtoZClinical.Core.Entities;

public class ClinicConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public string UomDefinitions { get; set; } = "Pcs,Box,Strip,Bottle";
    public string CurrencyCode { get; set; } = "USD";
    public string CurrencySymbol { get; set; } = "$";
    public string CurrencyName { get; set; } = "US Dollar";
    public string? VendorName { get; set; }
    public string? VendorPhone { get; set; }
    public string? VendorEmail { get; set; }
    public string? VendorAddress { get; set; }
    public string? OwnerName { get; set; }
    public string? OwnerPhone { get; set; }
    public string? OwnerEmail { get; set; }
    public string LanguageCode { get; set; } = "en";
    public string LanguageName { get; set; } = "English";
    public bool MaintenanceMode { get; set; }
    public string? MaintenanceNotes { get; set; }
    public string FormStyle { get; set; } = "Default";
    public string PrimaryColor { get; set; } = "#0b4f8a";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
}
