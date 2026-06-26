using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class FormPermissionService
{
    public const string VisibleFormsItemKey = "VisibleFormKeys";

    private readonly ClinicalDbContext _db;
    private readonly ClinicRuntimeCache _cache;

    public FormPermissionService(ClinicalDbContext db, ClinicRuntimeCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public string ResolveResponsibilityRole(ApplicationUser user)
    {
        if (user.IsVendorAdmin) return "Admin";
        return ClinicUserRoleHelper.ToResponsibilityRole(user.ClinicRole ?? ClinicUserRole.ClinicAdmin);
    }

    public Task<HashSet<string>> GetVisibleFormsAsync(Guid clinicId, string roleName) =>
        _cache.GetOrCreateAsync(ClinicRuntimeCache.VisibleFormsKey(clinicId, roleName), async () =>
        {
            var configured = await _db.RolePermissions
                .AsNoTracking()
                .Where(r => r.ClinicId == clinicId && r.RoleName == roleName)
                .ToListAsync();

            if (configured.Count == 0)
                return new HashSet<string>(ClinicalFormKeys.All, StringComparer.OrdinalIgnoreCase);

            return configured
                .Where(r => r.IsVisible)
                .Select(r => r.FormKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        });

    public static string? ResolveFormKeyFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.TrimEnd('/').ToLowerInvariant();

        return path switch
        {
            "/dashboard" or "/dashboard/index" => ClinicalFormKeys.Dashboard,
            "/workflow" or "/workflow/index" => ClinicalFormKeys.Workflow,
            "/patientregistration" or "/patientregistration/index" => ClinicalFormKeys.PatientRegistration,
            "/doctors" or "/doctors/index" => ClinicalFormKeys.Doctors,
            "/prescriptions" or "/prescriptions/index" => ClinicalFormKeys.Prescriptions,
            "/laboratory/registration" => ClinicalFormKeys.LabRegistration,
            "/laboratory/request" => ClinicalFormKeys.LabRequest,
            "/laboratory/result" => ClinicalFormKeys.LabResult,
            "/radiology/registration" => ClinicalFormKeys.RadiologyRegistration,
            "/radiology/request" => ClinicalFormKeys.RadiologyRequest,
            "/radiology/result" => ClinicalFormKeys.RadiologyResult,
            "/pharmacy/registration" => ClinicalFormKeys.PharmacyRegistration,
            "/pharmacy/request" => ClinicalFormKeys.PharmacyRequest,
            "/pharmacy/bill" => ClinicalFormKeys.PharmacyBill,
            "/pharmacy/purchase" => ClinicalFormKeys.PharmacyPurchaseBill,
            "/pharmacy/openingbalance" => ClinicalFormKeys.PharmacyOpeningBalance,
            "/serviceincomes" or "/serviceincomes/index" => ClinicalFormKeys.ServiceIncomes,
            "/serviceincomes/request" => ClinicalFormKeys.ServiceIncomeRequest,
            "/invoices" or "/invoices/index" => ClinicalFormKeys.Invoices,
            "/cashreceipts" or "/cashreceipts/index" => ClinicalFormKeys.CashReceipts,
            "/cashpayments" or "/cashpayments/index" => ClinicalFormKeys.CashPayments,
            "/chartofaccounts" or "/chartofaccounts/index" => ClinicalFormKeys.ChartAccounts,
            "/reports/patienthistory" => ClinicalFormKeys.PatientHistory,
            "/reports/appointmentreminders" => ClinicalFormKeys.AppointmentReminders,
            "/reports/patientstatus" => ClinicalFormKeys.PatientStatus,
            "/reports/plstatement" => ClinicalFormKeys.PlStatement,
            "/reports/costofgoodssold" => ClinicalFormKeys.CostOfGoodsSold,
            "/reports/balancesheet" => ClinicalFormKeys.BalanceSheet,
            "/reports/accountsreceivable" => ClinicalFormKeys.AccountsReceivable,
            "/reports/accountspayable" => ClinicalFormKeys.AccountsPayable,
            "/reports/operatingreport" => ClinicalFormKeys.OperatingReport,
            "/reports/cashreport" => ClinicalFormKeys.CashReport,
            "/reports/pharmacyinventory" => ClinicalFormKeys.PharmacyInventory,
            "/reports/doctorreport" => ClinicalFormKeys.DoctorReport,
            "/admin/responsibilities" => ClinicalFormKeys.RolePermissions,
            "/admin/auditlog" => ClinicalFormKeys.AuditLog,
            "/admin/backup" => ClinicalFormKeys.Backup,
            "/settings" or "/settings/index" or "/settings/users" or "/settings/uom"
                or "/settings/currency" or "/settings/language" or "/settings/owner"
                or "/settings/vendor" or "/settings/maintenance" or "/settings/changepassword"
                or "/settings/formstyle" => ClinicalFormKeys.Settings,
            "/search/query" => null,
            _ => null
        };
    }
}
