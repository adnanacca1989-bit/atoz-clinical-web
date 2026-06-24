using AtoZClinical.Core.Enums;
using Microsoft.AspNetCore.Identity;

namespace AtoZClinical.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public bool IsVendorAdmin { get; set; }
    public Guid? ClinicId { get; set; }
    public ClinicUserRole? ClinicRole { get; set; }
    public bool IsActive { get; set; } = true;
    public int UserNo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
