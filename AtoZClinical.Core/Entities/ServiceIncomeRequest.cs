namespace AtoZClinical.Core.Entities;

public class ServiceIncomeRequest : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int RequestNo { get; set; }
    public DateTime RequestDate { get; set; } = DateTime.Today;
    public string? PatientName { get; set; }
    public string? PatientBarcode { get; set; }
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string? DoctorName { get; set; }
    public string? Specialty { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
    public ICollection<ServiceIncomeRequestLine> Lines { get; set; } = [];
}

public class ServiceIncomeRequestLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServiceIncomeRequestId { get; set; }
    public int LineNo { get; set; }
    public int? ServiceNo { get; set; }
    public string? ServiceName { get; set; }
    public string? AccountName { get; set; }
    public int Qty { get; set; } = 1;
    public decimal Fee { get; set; }
    public decimal Total { get; set; }

    public ServiceIncomeRequest ServiceIncomeRequest { get; set; } = null!;
}
