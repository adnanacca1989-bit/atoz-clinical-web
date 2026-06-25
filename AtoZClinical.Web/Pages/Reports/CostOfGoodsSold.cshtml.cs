using AtoZClinical.Infrastructure.Services;

using AtoZClinical.Web.Services;

using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Mvc.RazorPages;



namespace AtoZClinical.Web.Pages.Reports;



public class CostOfGoodsSoldModel : PageModel

{

    private readonly PharmacyCogsService _cogs;

    private readonly ClinicContextService _clinicContext;



    public CostOfGoodsSoldModel(PharmacyCogsService cogs, ClinicContextService clinicContext)

    {

        _cogs = cogs;

        _clinicContext = clinicContext;

    }



    [BindProperty(SupportsGet = true)]

    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);



    [BindProperty(SupportsGet = true)]

    public DateTime ToDate { get; set; } = DateTime.Today;



    [BindProperty(SupportsGet = true)]

    public string? DoctorName { get; set; }



    [BindProperty(SupportsGet = true)]

    public string? PatientName { get; set; }



    public List<PharmacyCogsService.CogsRow> Results { get; private set; } = [];

    public decimal TotalCost { get; private set; }

    public decimal TotalSales { get; private set; }

    public decimal GrossMargin => TotalSales - TotalCost;



    public async Task<IActionResult> OnGetAsync() => await RunAsync();



    public Task<IActionResult> OnPostRunAsync() => RunAsync();



    public IActionResult OnPostClearAsync() => RedirectToPage();



    private async Task<IActionResult> RunAsync()

    {

        var clinicId = await _clinicContext.GetClinicIdAsync();

        if (clinicId is null) return Forbid();



        Results = await _cogs.GetCogsRowsAsync(clinicId.Value, FromDate, ToDate, DoctorName, PatientName);

        TotalCost = Results.Sum(r => r.TotalCost);

        TotalSales = Results.Sum(r => r.SalesAmount);

        return Page();

    }



    public async Task<IActionResult> OnPostExportAsync()

    {

        await RunAsync();

        var bytes = ReportExcelService.Export("Cost of Goods Sold",

            ["Date", "Bill No", "Patient", "Doctor", "Item", "Qty", "Unit Cost", "COGS", "Sales"],

            Results.Select(r => new object?[]

            {

                r.BillDate.ToString("d"), r.BillNo, r.PatientName, r.DoctorName, r.ItemName,

                r.Qty, r.UnitCost, r.TotalCost, r.SalesAmount

            }));

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",

            $"CostOfGoodsSold_{DateTime.Now:yyyyMMdd}.xlsx");

    }

}


