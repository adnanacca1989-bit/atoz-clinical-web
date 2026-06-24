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

    [BindProperty(SupportsGet = true)]
    public string? Section { get; set; }

    public PatientPrintBundle Bundle { get; private set; } = new();

    private static readonly Dictionary<string, string[]> SectionTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["registration"] = ["Patient Registration"],
        ["radiology"] = ["Radiology Request", "Radiology Result"],
        ["laboratory"] = ["Laboratory Request", "Laboratory Result"],
        ["prescription"] = ["Doctor's Prescription"],
        ["cashreceipt"] = ["Cash Receipt"],
        ["cashpayment"] = ["Cash Payment"],
        ["cash"] = ["Cash Receipt", "Cash Payment"]
    };

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        Bundle = await _bundleService.BuildAsync(clinicId.Value, PatientName, PatientId, DoctorName);

        if (!string.IsNullOrWhiteSpace(Section) && SectionTitles.TryGetValue(Section.Trim(), out var titles))
            Bundle.Sections = Bundle.Sections.Where(s => titles.Contains(s.Title)).ToList();

        return Page();
    }
}
