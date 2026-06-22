using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Reports;

public class PatientPrintBundleModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly PatientPrintBundleService _bundleService;

    public PatientPrintBundleModel(ClinicContextService clinicContext, PatientPrintBundleService bundleService)
    {
        _clinicContext = clinicContext;
        _bundleService = bundleService;
    }

    [BindProperty(SupportsGet = true)]
    public string? PatientName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PatientId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DoctorName { get; set; }

    public PatientPrintBundle Bundle { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        Bundle = await _bundleService.BuildAsync(clinicId.Value, PatientName, PatientId, DoctorName);
        return Page();
    }
}
