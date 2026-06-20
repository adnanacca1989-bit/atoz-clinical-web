using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Account;

public class LicenseBlockedModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Reason { get; set; }

    public string Title { get; private set; } = "Access Blocked";
    public string Message { get; private set; } = "You cannot access the system at this time.";
    public string? ClinicName { get; private set; }
    public DateTime? LicenseExpires { get; private set; }

    public void OnGet()
    {
        var blockReason = Enum.IsDefined(typeof(ClinicBlockReason), Reason)
            ? (ClinicBlockReason)Reason
            : ClinicBlockReason.NoClinic;

        (Title, Message) = blockReason switch
        {
            ClinicBlockReason.Pending => ("Registration Pending", "Your clinic account is waiting for vendor approval. You can login after activation."),
            ClinicBlockReason.Suspended => ("Account Suspended", "Your clinic account has been suspended."),
            ClinicBlockReason.Expired => ("License Expired", "Your subscription license has expired."),
            ClinicBlockReason.NoClinic => ("No Clinic Assigned", "This user is not linked to a clinic."),
            _ => ("Access Blocked", "You cannot access the system at this time.")
        };
    }
}
