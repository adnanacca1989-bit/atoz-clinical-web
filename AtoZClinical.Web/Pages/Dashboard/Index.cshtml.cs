using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Dashboard;

public class IndexModel : PageModel
{
    private readonly ClinicContextService _context;
    private readonly ClinicalDbContext _db;

    public IndexModel(ClinicContextService context, ClinicalDbContext db)
    {
        _context = context;
        _db = db;
    }

    public string ClinicName { get; private set; } = string.Empty;
    public int PatientCount { get; private set; }
    public int TodayAppointments { get; private set; }
    public int UpcomingAppointments { get; private set; }

    public IReadOnlyList<FormGroup> FormGroups { get; } =
    [
        new("Clinic", [
            new("/PatientRegistration/Index", "Patient Registration"),
            new("/Doctors/Index", "Doctor Registration"),
            new("/Prescriptions/Index", "Doctor's Prescription")
        ]),
        new("Laboratory", [
            new("/Laboratory/Registration", "Lab Registration"),
            new("/Laboratory/Request", "Lab Request"),
            new("/Laboratory/Result", "Lab Result")
        ]),
        new("Radiology", [
            new("/Radiology/Registration", "Radiology Registration"),
            new("/Radiology/Request", "Radiology Request"),
            new("/Radiology/Result", "Radiology Result")
        ]),
        new("Pharmacy", [
            new("/Pharmacy/Registration", "Pharmacy Registration"),
            new("/Pharmacy/Request", "Pharmacy Request"),
            new("/Pharmacy/Bill", "Pharmacy Bill"),
            new("/Pharmacy/Purchase", "Purchase Bill"),
            new("/Pharmacy/OpeningBalance", "Opening Balance")
        ]),
        new("Billing", [
            new("/ServiceIncomes/Index", "Service Income"),
            new("/Invoices/Index", "Invoice / Billing"),
            new("/CashReceipts/Index", "Cash Receipt"),
            new("/CashPayments/Index", "Cash Payment"),
            new("/ChartOfAccounts/Index", "Chart of Accounts")
        ]),
        new("Reports", [
            new("/Reports/PatientHistory", "Patient History"),
            new("/Reports/PatientStatus", "Patient Status"),
            new("/Reports/PlStatement", "PL Statement"),
            new("/Reports/AccountsReceivable", "Accounts Receivable"),
            new("/Reports/OperatingReport", "Operating Report"),
            new("/Reports/CashReport", "Cash Report"),
            new("/Reports/PharmacyInventory", "Pharmacy Inventory")
        ]),
        new("Admin", [
            new("/Admin/Responsibilities", "Responsibilities"),
            new("/Admin/AuditLog", "Audit Log"),
            new("/Admin/Backup", "Data Backup")
        ])
    ];

    public sealed record FormLink(string Page, string Label);
    public sealed record FormGroup(string Title, FormLink[] Forms);

    public async Task OnGetAsync()
    {
        var clinic = await _context.GetCurrentClinicAsync();
        ClinicName = clinic?.Name ?? "Clinic";
        if (clinic is null) return;

        var today = DateTime.Today;
        PatientCount = await _db.Patients.CountAsync(p => p.ClinicId == clinic.Id);
        TodayAppointments = await _db.Appointments.CountAsync(a => a.ClinicId == clinic.Id && a.AppointmentDate == today);
        UpcomingAppointments = await _db.Appointments.CountAsync(a =>
            a.ClinicId == clinic.Id && a.AppointmentDate > today && a.AppointmentDate <= today.AddDays(7));
    }
}
