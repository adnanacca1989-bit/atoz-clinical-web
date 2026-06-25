using AtoZClinical.Infrastructure.Billing;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Billing;

public class IndexModel : PageModel
{
    private readonly ClinicContextService _context;
    private readonly IStripeBillingService _billing;
    private readonly ClinicalAppUrls _urls;

    public IndexModel(ClinicContextService context, IStripeBillingService billing, ClinicalAppUrls urls)
    {
        _context = context;
        _billing = billing;
        _urls = urls;
    }

    public bool BillingEnabled => _billing.IsConfigured;
    public string ClinicName { get; private set; } = "";
    public string PlanName { get; private set; } = "";
    public string SubscriptionStatus { get; private set; } = "";
    public DateTime? LicenseExpires { get; private set; }
    public DateTime? TrialEndsAt { get; private set; }
    public IReadOnlyList<BillingPlan> Plans => BillingPlanCatalog.PaidPlans;
    public string? Message { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? success, string? canceled)
    {
        var clinic = await _context.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();

        ClinicName = clinic.Name;
        PlanName = clinic.PlanName;
        SubscriptionStatus = clinic.SubscriptionStatus;
        LicenseExpires = clinic.LicenseExpires;
        TrialEndsAt = clinic.TrialEndsAt ?? clinic.LicenseExpires;

        if (success == "1")
            Message = "Thank you — your subscription is being activated. It may take a minute to reflect.";
        else if (canceled == "1")
            Message = "Checkout was canceled. You can try again when ready.";

        return Page();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string planKey)
    {
        if (!_billing.IsConfigured)
            return RedirectToPage();

        var clinicId = await _context.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var successUrl = _urls.BuildPageUrl("Billing/Index", new Dictionary<string, string?> { ["success"] = "1" });
        var cancelUrl = _urls.BuildPageUrl("Billing/Index", new Dictionary<string, string?> { ["canceled"] = "1" });

        try
        {
            var url = await _billing.CreateCheckoutSessionAsync(clinicId.Value, planKey, successUrl, cancelUrl);
            if (!string.IsNullOrWhiteSpace(url))
                return Redirect(url);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            return await OnGetAsync(null, null);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostManageAsync()
    {
        if (!_billing.IsConfigured)
            return RedirectToPage();

        var clinicId = await _context.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var returnUrl = _urls.BuildPageUrl("Billing/Index");
        var url = await _billing.CreateCustomerPortalSessionAsync(clinicId.Value, returnUrl);
        if (!string.IsNullOrWhiteSpace(url))
            return Redirect(url);

        return RedirectToPage();
    }
}
