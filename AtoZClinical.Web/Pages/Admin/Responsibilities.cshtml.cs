using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Admin;

public class ResponsibilitiesModel : PageModel
{
    private readonly RolePermissionService _service;
    private readonly ClinicContextService _clinicContext;

    public ResponsibilitiesModel(RolePermissionService service, ClinicContextService clinicContext)
    {
        _service = service;
        _clinicContext = clinicContext;
    }

    [BindProperty]
    public string UserRole { get; set; } = "Admin";

    [BindProperty]
    public List<FormPermissionInput> Forms { get; set; } = [];

    public static readonly string[] Roles = ["Admin", "Doctor", "Reception", "Lab", "Radiology", "Cashier"];

    public static readonly (string Key, string Label)[] FormDefinitions =
    [
        (ClinicalFormKeys.Dashboard, "Dashboard"),
        (ClinicalFormKeys.Workflow, "Patient Process Workflow"),
        (ClinicalFormKeys.PatientRegistration, "Patient Registration"),
        (ClinicalFormKeys.Doctors, "Doctor Registration"),
        (ClinicalFormKeys.Prescriptions, "Doctor's Prescription"),
        (ClinicalFormKeys.LabRegistration, "Laboratory Registration"),
        (ClinicalFormKeys.LabRequest, "Laboratory Request"),
        (ClinicalFormKeys.LabResult, "Laboratory Result"),
        (ClinicalFormKeys.RadiologyRegistration, "Radiology Registration"),
        (ClinicalFormKeys.RadiologyRequest, "Radiology Request"),
        (ClinicalFormKeys.RadiologyResult, "Radiology Result"),
        (ClinicalFormKeys.ServiceIncomes, "Service Income"),
        (ClinicalFormKeys.Invoices, "Invoice / Billing"),
        (ClinicalFormKeys.CashReceipts, "Cash Receipt"),
        (ClinicalFormKeys.CashPayments, "Cash Payment"),
        (ClinicalFormKeys.ChartAccounts, "Chart of Accounts"),
        (ClinicalFormKeys.PharmacyRegistration, "Pharmacy Registration"),
        (ClinicalFormKeys.PharmacyRequest, "Pharmacy Request"),
        (ClinicalFormKeys.PharmacyBill, "Pharmacy Bill"),
        (ClinicalFormKeys.PharmacyPurchaseBill, "Pharmacy Purchase Bill"),
        (ClinicalFormKeys.PharmacyOpeningBalance, "Pharmacy Opening Balance"),
        (ClinicalFormKeys.PatientHistory, "Patient History"),
        (ClinicalFormKeys.AppointmentReminders, "Appointment Reminders"),
        (ClinicalFormKeys.PatientStatus, "Patient Status Report"),
        (ClinicalFormKeys.PlStatement, "PL Statement"),
        (ClinicalFormKeys.CostOfGoodsSold, "Cost of Goods Sold"),
        (ClinicalFormKeys.BalanceSheet, "Balance Sheet"),
        (ClinicalFormKeys.AccountsReceivable, "Accounts Receivable"),
        (ClinicalFormKeys.AccountsPayable, "Accounts Payable"),
        (ClinicalFormKeys.OperatingReport, "Operating Report"),
        (ClinicalFormKeys.CashReport, "Cash Report"),
        (ClinicalFormKeys.PharmacyInventory, "Pharmacy Inventory Report"),
        (ClinicalFormKeys.Settings, "Settings"),
        (ClinicalFormKeys.RolePermissions, "Responsibilities"),
        (ClinicalFormKeys.AuditLog, "Audit Log"),
        (ClinicalFormKeys.Backup, "Data Backup")
    ];

    public async Task<IActionResult> OnGetAsync(string? role = null)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();
        UserRole = role ?? UserRole;
        await LoadFormsAsync(clinicId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var items = Forms.Select(f => new Core.Entities.RolePermission
        {
            FormKey = f.FormKey,
            IsVisible = f.IsVisible
        });
        await _service.SaveBulkAsync(clinicId.Value, UserRole, items, User.Identity?.Name);
        return RedirectToPage(new { role = UserRole });
    }

    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task LoadFormsAsync(Guid clinicId)
    {
        var existing = await _service.ListForRoleAsync(clinicId, UserRole);
        Forms = FormDefinitions.Select(fd =>
        {
            var match = existing.FirstOrDefault(e => e.FormKey == fd.Key);
            return new FormPermissionInput
            {
                FormKey = fd.Key,
                FormLabel = fd.Label,
                IsVisible = match?.IsVisible ?? true
            };
        }).ToList();
    }

    public sealed class FormPermissionInput
    {
        public string FormKey { get; set; } = string.Empty;
        public string FormLabel { get; set; } = string.Empty;
        public bool IsVisible { get; set; } = true;
    }
}
