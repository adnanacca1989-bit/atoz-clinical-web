using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Reports;

public class PharmacyInventoryModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly PharmacyInventoryService _inventory;

    public PharmacyInventoryModel(ClinicContextService clinicContext, PharmacyInventoryService inventory)
    {
        _clinicContext = clinicContext;
        _inventory = inventory;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public List<PharmacyInventoryService.PharmacyInventoryReportRow> Results { get; private set; } = [];
    public decimal TotalValue { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();

    public Task<IActionResult> OnPostRunAsync() => RunAsync();

    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        Results = await _inventory.GetReportAsync(clinicId.Value, FromDate, ToDate, Search);
        TotalValue = Results.Sum(r => r.TotalValue);
        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        var bytes = ReportExcelService.Export("Pharmacy Inventory",
            ["Item No", "Barcode", "Medicine", "Qty In", "Qty Out", "Balance", "Unit Cost", "Total Value"],
            Results.Select(r => new object?[]
            {
                r.ItemNo, r.Barcode, r.MedicineName, r.QtyIn, r.QtyOut, r.QtyBalance, r.MovingAverageCost, r.TotalValue
            }));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"PharmacyInventory_{DateTime.Now:yyyyMMdd}.xlsx");
    }
}
