using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

public sealed class FormPermissionService
{
    public const string VisibleFormsItemKey = "VisibleFormKeys";

    private readonly ClinicalDbContext _db;
    private readonly ClinicRuntimeCache _cache;
    private readonly ILogger<FormPermissionService> _logger;

    public FormPermissionService(
        ClinicalDbContext db,
        ClinicRuntimeCache cache,
        ILogger<FormPermissionService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public string ResolveResponsibilityRole(ApplicationUser user)
    {
        if (user is null)
            return "Admin";

        if (user.IsVendorAdmin)
            return "Admin";

        return ClinicUserRoleHelper.ToResponsibilityRole(user.ClinicRole ?? ClinicUserRole.ClinicAdmin);
    }

    public async Task<HashSet<string>> GetVisibleFormsAsync(Guid clinicId, string roleName)
    {
        if (clinicId == Guid.Empty || string.IsNullOrWhiteSpace(roleName))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var cacheKey = ClinicRuntimeCache.VisibleFormsKey(clinicId, roleName);
            if (_cache.TryGet<HashSet<string>>(cacheKey, out var cached) && cached is { Count: > 0 })
                return cached;

            var visible = await LoadVisibleFormsAsync(clinicId, roleName);
            if (visible.Count > 0)
            {
                _cache.SetWithTtl(cacheKey, visible);
                return visible;
            }

            _cache.InvalidateVisibleForms(clinicId, roleName);
            return visible;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resolve visible forms for clinic {ClinicId} role {Role}. Returning empty set.",
                clinicId, roleName);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<HashSet<string>> LoadVisibleFormsAsync(Guid clinicId, string roleName)
    {
        var visible = await BuildVisibleSetAsync(clinicId, roleName);
        if (visible.Count > 0)
            return visible;

        _cache.InvalidateVisibleForms(clinicId, roleName);
        var repaired = await RolePermissionBootstrap.TryRepairAsync(_db, _cache, clinicId, roleName, logger: _logger);
        if (repaired)
            visible = await BuildVisibleSetAsync(clinicId, roleName);

        if (visible.Count == 0)
        {
            _logger.LogWarning(
                "No visible permissions for clinic {ClinicId} role {Role} after repair attempt.",
                clinicId, roleName);
        }

        return visible;
    }

    private async Task<HashSet<string>> BuildVisibleSetAsync(Guid clinicId, string roleName)
    {
        try
        {
            var configured = await LoadConfiguredPermissionsAsync(clinicId, roleName);

            if (configured.Count == 0)
            {
                if (roleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                    return new HashSet<string>(ClinicalFormKeys.All, StringComparer.OrdinalIgnoreCase);

                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return configured
                .Where(r => r.IsVisible)
                .Select(r => r.FormKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to build visible permission set for clinic {ClinicId} role {Role}.",
                clinicId, roleName);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<List<Core.Entities.RolePermission>> LoadConfiguredPermissionsAsync(Guid clinicId, string roleName)
    {
        var configured = await _db.RolePermissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.ClinicId == clinicId && r.RoleName == roleName)
            .ToListAsync();

        if (configured.Count == 0 && roleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            configured = await _db.RolePermissions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => r.ClinicId == clinicId && r.RoleName == ClinicalRoles.ClinicAdmin)
                .ToListAsync();
        }

        return configured;
    }

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
            "/expenses" or "/expenses/index" => ClinicalFormKeys.Expenses,
            "/chartofaccounts" or "/chartofaccounts/index" => ClinicalFormKeys.ChartAccounts,
            "/reports/patienthistory" => ClinicalFormKeys.PatientHistory,
            "/reports/appointmentreminders" => ClinicalFormKeys.AppointmentReminders,
            "/reports/patientstatus" => ClinicalFormKeys.PatientStatus,
            "/reports/plstatement" => ClinicalFormKeys.PlStatement,
            "/reports/generalledger" => ClinicalFormKeys.GeneralLedger,
            "/reports/trialbalance" => ClinicalFormKeys.TrialBalance,
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
            "/messages" or "/messages/index" => ClinicalFormKeys.Messaging,
            "/search/query" => null,
            _ => null
        };
    }
}
