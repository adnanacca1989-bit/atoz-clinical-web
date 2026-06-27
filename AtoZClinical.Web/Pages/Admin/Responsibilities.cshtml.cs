using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Admin;

public class ResponsibilitiesModel : PageModel
{
    private readonly RolePermissionService _service;
    private readonly ClinicContextService _clinicContext;
    private readonly UserManager<ApplicationUser> _users;

    public ResponsibilitiesModel(
        RolePermissionService service,
        ClinicContextService clinicContext,
        UserManager<ApplicationUser> users)
    {
        _service = service;
        _clinicContext = clinicContext;
        _users = users;
    }

    [BindProperty]
    public string UserRole { get; set; } = "Admin";

    [BindProperty]
    public string? SelectedUserId { get; set; }

    [BindProperty]
    public List<FormPermissionInput> Forms { get; set; } = [];

    public List<ApplicationUser> ClinicUsers { get; private set; } = [];

    public static readonly string[] Roles = ClinicUserRoleHelper.ResponsibilityRoles;

    public static readonly (string Key, string Label)[] FormDefinitions =
    [
        (ClinicalFormKeys.Dashboard, "Dashboard"),
        (ClinicalFormKeys.Messaging, "Internal Messaging"),
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
        (ClinicalFormKeys.ServiceIncomeRequest, "Service Income Request"),
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
        (ClinicalFormKeys.DoctorReport, "Doctor Report"),
        (ClinicalFormKeys.Settings, "Settings"),
        (ClinicalFormKeys.RolePermissions, "Responsibilities"),
        (ClinicalFormKeys.AuditLog, "Audit Log"),
        (ClinicalFormKeys.Backup, "Data Backup")
    ];

    public async Task<IActionResult> OnGetAsync(string? role = null, string? userId = null)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        await LoadUsersAsync(clinicId.Value);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            SelectedUserId = userId;
            var user = ClinicUsers.FirstOrDefault(u => u.Id == userId);
            if (user?.ClinicRole is not null)
                UserRole = ClinicUserRoleHelper.ToResponsibilityRole(user.ClinicRole.Value);
        }
        else if (!string.IsNullOrWhiteSpace(role))
        {
            UserRole = role;
        }

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

        if (!string.IsNullOrWhiteSpace(SelectedUserId))
        {
            var user = await _users.Users.FirstOrDefaultAsync(u => u.Id == SelectedUserId && u.ClinicId == clinicId);
            if (user is not null)
            {
                user.ClinicRole = ClinicUserRoleHelper.ParseResponsibilityRole(UserRole);
                var result = await _users.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors)
                        ModelState.AddModelError(string.Empty, error.Description);
                    await LoadUsersAsync(clinicId.Value);
                    await LoadFormsAsync(clinicId.Value);
                    return Page();
                }
            }
        }

        return RedirectToPage(new { role = UserRole, userId = SelectedUserId });
    }

    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task LoadUsersAsync(Guid clinicId)
    {
        ClinicUsers = await _users.Users
            .Where(u => u.ClinicId == clinicId && u.IsActive)
            .OrderBy(u => u.UserName)
            .ToListAsync();
    }

    private async Task LoadFormsAsync(Guid clinicId)
    {
        var existing = await _service.ListForRoleAsync(clinicId, UserRole);
        if (existing.Count == 0 && UserRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            existing = await _service.ListForRoleAsync(clinicId, ClinicalRoles.ClinicAdmin);

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
