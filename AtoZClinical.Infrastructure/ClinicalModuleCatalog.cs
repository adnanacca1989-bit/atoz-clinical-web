using AtoZClinical.Infrastructure.Services;

namespace AtoZClinical.Infrastructure;

public static class ClinicalModuleCatalog
{
    public sealed record ModuleGroup(string Key, string Label, string[] FormKeys);

    /// <summary>Coarse module bundles for clinic registration / SaaS enablement.</summary>
    public static readonly ModuleGroup[] Groups =
    [
        new("Outpatient", "Outpatient / Clinic", [
            ClinicalFormKeys.Dashboard, ClinicalFormKeys.Workflow, ClinicalFormKeys.Messaging,
            ClinicalFormKeys.PatientRegistration, ClinicalFormKeys.Doctors, ClinicalFormKeys.Prescriptions
        ]),
        new("Inpatient", "Inpatient / Ward", [
            ClinicalFormKeys.DoctorSurgery, ClinicalFormKeys.BookRoom, ClinicalFormKeys.PatientWardRoom,
            ClinicalFormKeys.WardPatientReport
        ]),
        new("Laboratory", "Laboratory", [
            ClinicalFormKeys.LabRegistration, ClinicalFormKeys.LabRequest, ClinicalFormKeys.LabResult
        ]),
        new("Radiology", "Radiology", [
            ClinicalFormKeys.RadiologyRegistration, ClinicalFormKeys.RadiologyRequest, ClinicalFormKeys.RadiologyResult
        ]),
        new("Pharmacy", "Pharmacy", [
            ClinicalFormKeys.PharmacyRegistration, ClinicalFormKeys.PharmacyOpeningBalance,
            ClinicalFormKeys.PharmacyPurchaseBill, ClinicalFormKeys.PharmacyRequest, ClinicalFormKeys.PharmacyBill
        ]),
        new("Billing", "Billing", [
            ClinicalFormKeys.ServiceIncomes, ClinicalFormKeys.ServiceIncomeRequest, ClinicalFormKeys.Invoices,
            ClinicalFormKeys.CashReceipts, ClinicalFormKeys.CashPayments, ClinicalFormKeys.ChartAccounts, ClinicalFormKeys.Expenses
        ]),
        new("Reports", "Reports", [
            ClinicalFormKeys.PatientHistory, ClinicalFormKeys.AppointmentReminders, ClinicalFormKeys.PatientStatus,
            ClinicalFormKeys.PlStatement, ClinicalFormKeys.GeneralLedger, ClinicalFormKeys.TrialBalance,
            ClinicalFormKeys.CostOfGoodsSold, ClinicalFormKeys.BalanceSheet,
            ClinicalFormKeys.AccountsReceivable, ClinicalFormKeys.AccountsPayable,
            ClinicalFormKeys.CashReport, ClinicalFormKeys.PharmacyInventory, ClinicalFormKeys.DoctorReport,
            ClinicalFormKeys.RequestReport
        ]),
        new("Admin", "Clinic Admin", [
            ClinicalFormKeys.RolePermissions, ClinicalFormKeys.AuditLog, ClinicalFormKeys.Backup, ClinicalFormKeys.Settings
        ])
    ];

    public static HashSet<string> AllFormKeys() =>
        Groups.SelectMany(g => g.FormKeys).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> LegacyGroupKeyAliases =
        new(StringComparer.OrdinalIgnoreCase) { ["Core"] = "Outpatient" };

    public static string BuildEnabledFormKeysFromGroups(IEnumerable<string>? groupKeys)
    {
        var selected = groupKeys?
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => LegacyGroupKeyAliases.TryGetValue(g.Trim(), out var mapped) ? mapped : g.Trim())
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
