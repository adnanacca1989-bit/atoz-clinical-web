using AtoZClinical.Core.Enums;

namespace AtoZClinical.Core.Entities;

/// <summary>
/// Each clinic is a customer you sell the system to.
/// Store hosting and login details here to hand off to the client.
/// </summary>
public class Clinic
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ClinicCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }

    /// <summary>Web address the client uses, e.g. https://clinic.example.com</summary>
    public string? HostingUrl { get; set; }
    public string? DatabaseHost { get; set; }
    public int DatabasePort { get; set; } = 5432;
    public string? DatabaseName { get; set; }
    public string? LicenseKey { get; set; }
    public DateTime? LicenseExpires { get; set; }
    /// <summary>SaaS plan label shown to vendor and clinic (Trial, Standard, Professional).</summary>
    public string PlanName { get; set; } = "Standard";
    /// <summary>Maximum users allowed for this clinic subscription.</summary>
    public int MaxUsers { get; set; } = 25;
    public ClinicStatus Status { get; set; } = ClinicStatus.Active;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Patient> Patients { get; set; } = [];
    public ICollection<Appointment> Appointments { get; set; } = [];
}
