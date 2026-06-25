namespace AtoZClinical.Core.Entities;

public class PharmacyRequest : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int RequestNo { get; set; }
    public DateTime RequestDate { get; set; } = DateTime.Today;
    public int? PrescriptionNo { get; set; }
    public string? PatientName { get; set; }
    public string? PatientId { get; set; }
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string? DoctorName { get; set; }
    public string? Specialty { get; set; }
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
    public ICollection<PharmacyRequestLine> Lines { get; set; } = [];
}

public class PharmacyRequestLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PharmacyRequestId { get; set; }
    public int LineNo { get; set; }
    public string? Barcode { get; set; }
    public string? MedicineCode { get; set; }
    public string? MedicineName { get; set; }
    public string? Dosage { get; set; }
    public string? Uom { get; set; }
    public int Qty { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }

    public PharmacyRequest PharmacyRequest { get; set; } = null!;
}
