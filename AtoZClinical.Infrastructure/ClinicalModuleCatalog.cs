using AtoZClinical.Infrastructure.Services;

namespace AtoZClinical.Infrastructure;

public static class ClinicalModuleCatalog
{
    public sealed record ModuleGroup(string Key, string Label, string[] FormKeys);

    public static readonly ModuleGroup[] Groups =
    [
        new("Core", "Core Clinic", [
            ClinicalFormKeys.Dashboard, ClinicalFormKeys.Workflow,
            ClinicalFormKeys.PatientRegistration, ClinicalFormKeys.Doctors,
            ClinicalFormKeys.Prescriptions, ClinicalFormKeys.Messaging
        ]),
        new("Laboratory", "Laboratory", [
            ClinicalFormKeys.LabRegistration, ClinicalFormKeys.LabRequest, ClinicalFormKeys.LabResult
        ]),
        new("Radiology", "Radiology", [
            ClinicalFormKeys.RadiologyRegistration, ClinicalFormKeys.RadiologyRequest, ClinicalFormKeys.RadiologyResult
        ]),
        new("Pharmacy", "Pharmacy", [
            ClinicalFormKeys.PharmacyRegistration, ClinicalFormKeys.PharmacyRequest,
            ClinicalFormKeys.PharmacyBill, ClinicalFormKeys.PharmacyPurchaseBill, ClinicalFormKeys.PharmacyOpeningBalance
        ]),
        new("Billing", "Billing", [
            ClinicalFormKeys.ServiceIncomes, ClinicalFormKeys.ServiceIncomeRequest, ClinicalFormKeys.Invoices,
            ClinicalFormKeys.CashReceipts, ClinicalFormKeys.CashPayments, ClinicalFormKeys.ChartAccounts, ClinicalFormKeys.Expenses
        ]),
        new("Reports", "Reports", [
            ClinicalFormKeys.PatientHistory, ClinicalFormKeys.AppointmentReminders, ClinicalFormKeys.PatientStatus,
            ClinicalFormKeys.PlStatement, ClinicalFormKeys.GeneralLedger, ClinicalFormKeys.TrialBalance, ClinicalFormKeys.CostOfGoodsSold, ClinicalFormKeys.BalanceSheet,
            ClinicalFormKeys.AccountsReceivable, ClinicalFormKeys.AccountsPayable, ClinicalFormKeys.OperatingReport,
            ClinicalFormKeys.CashReport, ClinicalFormKeys.PharmacyInventory, ClinicalFormKeys.DoctorReport,
            ClinicalFormKeys.RequestReport
        ]),
        new("Admin", "Admin", [
            ClinicalFormKeys.RolePermissions, ClinicalFormKeys.AuditLog, ClinicalFormKeys.Backup, ClinicalFormKeys.Settings
        ])
    ];

    public static HashSet<string> AllFormKeys() =>
        Groups.SelectMany(g => g.FormKeys).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static string BuildEnabledFormKeysFromGroups(IEnumerable<string>? groupKeys)
    {
        var selected = groupKeys?
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .ToList();

        if (selected is null || selected.Count == 0)
            return ClinicModuleService.SerializeEnabledForms(AllFormKeys());

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in Groups.Where(g => selected.Contains(g.Key, StringComparer.OrdinalIgnoreCase)))
        {
            foreach (var key in group.FormKeys)
                keys.Add(key);
        }

        if (keys.Count == 0)
            return ClinicModuleService.SerializeEnabledForms(AllFormKeys());

        return ClinicModuleService.SerializeEnabledForms(keys);
    }
}
