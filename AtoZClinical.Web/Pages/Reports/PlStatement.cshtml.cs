using AtoZClinical.Infrastructure.Data;

using AtoZClinical.Infrastructure.Services;

using AtoZClinical.Web.Services;

using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.EntityFrameworkCore;



namespace AtoZClinical.Web.Pages.Reports;



public class PlStatementModel : PageModel

{

    private readonly ClinicalDbContext _db;

    private readonly ClinicContextService _clinicContext;

    private readonly PharmacyCogsService _cogs;



    public PlStatementModel(ClinicalDbContext db, ClinicContextService clinicContext, PharmacyCogsService cogs)

    {

        _db = db;

        _clinicContext = clinicContext;

        _cogs = cogs;

    }



    [BindProperty(SupportsGet = true)]

    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);



    [BindProperty(SupportsGet = true)]

    public DateTime ToDate { get; set; } = DateTime.Today;



    [BindProperty(SupportsGet = true)]

    public string? DoctorName { get; set; }



    [BindProperty(SupportsGet = true)]

    public string? PatientName { get; set; }



    [BindProperty(SupportsGet = true)]

    public bool NonZero { get; set; }



    public List<PlRow> Results { get; private set; } = [];

    public decimal NetRevenue { get; private set; }

    public decimal CostOfGoodsSold { get; private set; }

    public decimal Expenses { get; private set; }

    public decimal NetProfit => NetRevenue - CostOfGoodsSold - Expenses;



    public async Task<IActionResult> OnGetAsync() => await RunAsync();



    public Task<IActionResult> OnPostRunAsync() => RunAsync();



    public IActionResult OnPostClearAsync() => RedirectToPage();



    private async Task<IActionResult> RunAsync()

    {

        var clinicId = await _clinicContext.GetClinicIdAsync();

        if (clinicId is null) return Forbid();



        var invoices = await _db.Invoices

            .Include(i => i.Lines)

            .Where(i => i.ClinicId == clinicId && i.InvoiceDate >= FromDate.Date && i.InvoiceDate <= ToDate.Date)

            .ToListAsync();



        if (!string.IsNullOrWhiteSpace(DoctorName))

            invoices = invoices.Where(i => i.DoctorName?.Contains(DoctorName, StringComparison.OrdinalIgnoreCase) == true).ToList();

        if (!string.IsNullOrWhiteSpace(PatientName))

            invoices = invoices.Where(i => i.PatientName?.Contains(PatientName, StringComparison.OrdinalIgnoreCase) == true).ToList();



        foreach (var inv in invoices)

        {

            foreach (var line in inv.Lines)

            {

                Results.Add(new PlRow("Revenue", "Clinical Revenue", line.ServiceName ?? "Service", line.LineTotal));

            }

        }



        var cogsRows = await _cogs.GetCogsRowsAsync(clinicId.Value, FromDate, ToDate, DoctorName, PatientName);

        foreach (var row in cogsRows.GroupBy(r => r.ItemName))

        {

            var total = row.Sum(r => r.TotalCost);

            Results.Add(new PlRow("COGS", "Cost of Goods Sold", row.Key, total));

        }



        var payments = await _db.CashPayments

            .Where(p => p.ClinicId == clinicId && p.PaymentDate >= FromDate.Date && p.PaymentDate <= ToDate.Date)

            .ToListAsync();



        foreach (var pay in payments)

            Results.Add(new PlRow("Expense", pay.ChartAccountName ?? "Expense", pay.Description ?? pay.PayeeName ?? "Payment", pay.Amount));



        if (NonZero)

            Results = Results.Where(r => r.Amount != 0).ToList();



        NetRevenue = Results.Where(r => r.Section == "Revenue").Sum(r => r.Amount);

        CostOfGoodsSold = Results.Where(r => r.Section == "COGS").Sum(r => r.Amount);

        Expenses = Results.Where(r => r.Section == "Expense").Sum(r => r.Amount);

        return Page();

    }



    public async Task<IActionResult> OnPostExportAsync()

    {

        await RunAsync();

        var bytes = ReportExcelService.Export("PL Statement",

            ["Section", "Account", "Detail", "Amount"],

            Results.Select(r => new object?[] { r.Section, r.Account, r.Detail, r.Amount }));

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",

            $"PLStatement_{DateTime.Now:yyyyMMdd}.xlsx");

    }



    public sealed record PlRow(string Section, string Account, string Detail, decimal Amount);

}


