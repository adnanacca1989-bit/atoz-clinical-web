namespace AtoZClinical.Infrastructure;

/// <summary>Default visible forms per responsibility role when none have been configured yet.</summary>
public static class RolePermissionDefaults
{
    private static readonly HashSet<string> DoctorForms =
    [
        ClinicalFormKeys.Dashboard,
        ClinicalFormKeys.Messaging,
        ClinicalFormKeys.Workflow,
        ClinicalFormKeys.PatientRegistration,
        ClinicalFormKeys.Prescriptions,
        ClinicalFormKeys.DoctorSurgery,
        ClinicalFormKeys.BookRoom,
        ClinicalFormKeys.PatientWardRoom,
        ClinicalFormKeys.LabRequest,
        ClinicalFormKeys.LabResult,
        ClinicalFormKeys.RadiologyRequest,
        ClinicalFormKeys.RadiologyResult,
        ClinicalFormKeys.ServiceIncomes,
        ClinicalFormKeys.ServiceIncomeRequest,
        ClinicalFormKeys.PharmacyRequest,
        ClinicalFormKeys.PatientHistory,
        ClinicalFormKeys.AppointmentReminders,
        ClinicalFormKeys.PatientStatus,
        ClinicalFormKeys.DoctorReport,
        ClinicalFormKeys.RequestReport,
        ClinicalFormKeys.Invoices
    ];

    private static readonly HashSet<string> ReceptionForms =
    [
        ClinicalFormKeys.Dashboard,
        ClinicalFormKeys.Messaging,
        ClinicalFormKeys.Workflow,
        ClinicalFormKeys.PatientRegistration,
        ClinicalFormKeys.ServiceIncomeRequest,
        ClinicalFormKeys.AppointmentReminders,
        ClinicalFormKeys.PatientStatus,
        ClinicalFormKeys.RequestReport,
        ClinicalFormKeys.CashReceipts,
        ClinicalFormKeys.BookRoom,
        ClinicalFormKeys.PatientWardRoom,
        ClinicalFormKeys.Invoices
    ];

    private static readonly HashSet<string> LabForms =
    [
        ClinicalFormKeys.Dashboard,
        ClinicalFormKeys.Messaging,
        ClinicalFormKeys.PatientRegistration,
        ClinicalFormKeys.LabRegistration,
        ClinicalFormKeys.LabRequest,
        ClinicalFormKeys.LabResult,
        ClinicalFormKeys.RequestReport
    ];

    private static readonly HashSet<string> RadiologyForms =
    [
        ClinicalFormKeys.Dashboard,
        ClinicalFormKeys.Messaging,
        ClinicalFormKeys.PatientRegistration,
        ClinicalFormKeys.RadiologyRegistration,
        ClinicalFormKeys.RadiologyRequest,
        ClinicalFormKeys.RadiologyResult,
        ClinicalFormKeys.RequestReport
    ];

    private static readonly HashSet<string> CashierForms =
    [
        ClinicalFormKeys.Dashboard,
        ClinicalFormKeys.Messaging,
        ClinicalFormKeys.PatientRegistration,
        ClinicalFormKeys.PharmacyRegistration,
        ClinicalFormKeys.PharmacyRequest,
        ClinicalFormKeys.PharmacyBill,
        ClinicalFormKeys.RequestReport,
        ClinicalFormKeys.CashReceipts,
        ClinicalFormKeys.Invoices
    ];

    public static IReadOnlyDictionary<string, HashSet<string>> ByRole { get; } =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Doctor"] = DoctorForms,
            ["Reception"] = ReceptionForms,
            ["Lab"] = LabForms,
            ["Radiology"] = RadiologyForms,
            ["Cashier"] = CashierForms
        };

    public static IEnumerable<RolePermissionSeed> SeedsForRole(string roleName)
    {
        var visible = ByRole.TryGetValue(roleName, out var set) ? set : null;
        foreach (var formKey in ClinicalFormKeys.All)
        {
            yield return new RolePermissionSeed(formKey, visible?.Contains(formKey) == true);
        }
    }

    public readonly record struct RolePermissionSeed(string FormKey, bool IsVisible);
}
