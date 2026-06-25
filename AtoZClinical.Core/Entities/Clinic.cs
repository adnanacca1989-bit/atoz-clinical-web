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
    /// <summary>Custom subdomain slug, e.g. "acme" for acme.yourapp.com</summary>
    public string? Subdomain { get; set; }
    /// <summary>Optional dedicated database connection name for enterprise tenants.</summary>
    public string? DedicatedConnectionName { get; set; }
    public string? DatabaseHost { get; set; }
    public int DatabasePort { get; set; } = 5432;
    public string? DatabaseName { get; set; }
    public string? LicenseKey { get; set; }
    public DateTime? LicenseExpires { get; set; }
    /// <summary>SaaS plan label shown to vendor and clinic (Trial, Standard, Professional).</summary>
    public string PlanName { get; set; } = "Standard";
    /// <summary>Maximum users allowed for this clinic subscription.</summary>
    public int MaxUsers { get; set; } = 25;

    /// <summary>Stripe customer id for SaaS billing.</summary>
    public string? StripeCustomerId { get; set; }
    /// <summary>Stripe subscription id when on a paid plan.</summary>
    public string? StripeSubscriptionId { get; set; }
    /// <summary>Stripe subscription status: none, trialing, active, past_due, canceled, unpaid.</summary>
    public string SubscriptionStatus { get; set; } = "none";
    /// <summary>When the self-service trial ends (may mirror LicenseExpires for trial plans).</summary>
    public DateTime? TrialEndsAt { get; set; }
    /// <summary>Last trial reminder email sent (dedup).</summary>
    public DateTime? LastTrialReminderSentAt { get; set; }
    /// <summary>Bitmask of onboarding emails sent (1=welcome, 2=day3, 4=day7).</summary>
    public int OnboardingEmailsSent { get; set; }

    public ClinicStatus Status { get; set; } = ClinicStatus.Active;
    public string? Notes { get; set; }
    /// <summary>JSON array of ClinicalFormKeys enabled for this clinic subscription.</summary>
    public string? EnabledFormKeys { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Patient> Patients { get; set; } = [];
    public ICollection<Appointment> Appointments { get; set; } = [];
}
