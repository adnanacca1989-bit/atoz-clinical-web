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
    public decimal ConsultationRevenue { get; private set; }
    public decimal LabRevenue { get; private set; }
    public decimal RadiologyRevenue { get; private set; }
    public decimal PharmacyRevenue { get; private set; }
    public decimal NetRevenue { get; private set; }
    public decimal CostOfGoodsSold { get; private set; }
    public decimal Expenses { get; private set; }
    public decimal GrossProfit => NetRevenue - CostOfGoodsSold;
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
                switch (ClassifyRevenue(line.ServiceName))
                {
                    case RevenueBucket.Lab:
                        LabRevenue += line.LineTotal;
                        break;
                    case RevenueBucket.Radiology:
                        RadiologyRevenue += line.LineTotal;
                        break;
                    case RevenueBucket.Pharmacy:
                        PharmacyRevenue += line.LineTotal;
                        break;
                    default:
                        ConsultationRevenue += line.LineTotal;
                        break;
                }
            }
        }

        AddRevenueRow("Consultation Revenue", ConsultationRevenue);
        AddRevenueRow("Lab Revenue", LabRevenue);
        AddRevenueRow("Radiology Revenue", RadiologyRevenue);
        AddRevenueRow("Pharmacy Bill Revenue", PharmacyRevenue);

        CostOfGoodsSold = await _cogs.GetTotalCogsAsync(clinicId.Value, FromDate, ToDate, DoctorName, PatientName);

        var payments = await _db.CashPayments
            .Where(p => p.ClinicId == clinicId && p.PaymentDate >= FromDate.Date && p.PaymentDate <= ToDate.Date)
            .ToListAsync();

        foreach (var pay in payments)
            Results.Add(new PlRow("Expense", pay.ChartAccountName ?? "Expense", pay.Description ?? pay.PayeeName ?? "Payment", pay.Amount));

        if (NonZero)
            Results = Results.Where(r => r.Amount != 0).ToList();

        NetRevenue = ConsultationRevenue + LabRevenue + RadiologyRevenue + PharmacyRevenue;
        Expenses = Results.Where(r => r.Section == "Expense").Sum(r => r.Amount);
        return Page();
    }

    private void AddRevenueRow(string label, decimal amount)
    {
        if (!NonZero || amount != 0)
            Results.Add(new PlRow("Revenue", "Income", label, amount));
    }

    private static RevenueBucket ClassifyRevenue(string? serviceName)
    {
        var name = serviceName ?? "";
        if (name.Contains("Pharmacy", StringComparison.OrdinalIgnoreCase))
            return RevenueBucket.Pharmacy;
        if (name.StartsWith("Lab", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Laboratory", StringComparison.OrdinalIgnoreCase))
            return RevenueBucket.Lab;
        if (name.StartsWith("Radiology", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radiology", StringComparison.OrdinalIgnoreCase))
            return RevenueBucket.Radiology;
        return RevenueBucket.Consultation;
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        var exportRows = new List<object?[]>
        {
            new object?[] { "Income", "Consultation Revenue", ConsultationRevenue },
            new object?[] { "Income", "Lab Revenue", LabRevenue },
            new object?[] { "Income", "Radiology Revenue", RadiologyRevenue },
            new object?[] { "Income", "Pharmacy Bill Revenue", PharmacyRevenue },
            new object?[] { "Income", "Total Income", NetRevenue },
            new object?[] { "COGS", "Cost of Goods Sold", CostOfGoodsSold },
            new object?[] { "Summary", "Gross Profit", GrossProfit }
        };
        exportRows.AddRange(Results.Where(r => r.Section == "Expense")
            .Select(r => new object?[] { "Expense", r.Detail, r.Amount }));
        exportRows.Add(new object?[] { "Summary", "Total Expenses", Expenses });
        exportRows.Add(new object?[] { "Summary", "Net Income", NetProfit });

        var bytes = ReportExcelService.Export("PL Statement",
            ["Section", "Account / Detail", "Amount"],
            exportRows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"PLStatement_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    private enum RevenueBucket { Consultation, Lab, Radiology, Pharmacy }

    public sealed record PlRow(string Section, string Account, string Detail, decimal Amount);
}
