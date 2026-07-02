namespace AtoZClinical.Infrastructure;

/// <summary>
/// Single source of truth for sidebar navigation, permission labels, and module grouping order.
/// Each <see cref="NavLink.FormKey"/> maps 1:1 to <see cref="ClinicalFormKeys"/> when set.
/// </summary>
public static class ClinicalNavCatalog
{
    public sealed record NavLink(string Label, string Page, string? FormKey = null, bool RequiresPermission = true);

    public sealed record NavGroup(string? SubLabel, NavLink[] Links);

    public sealed record NavSection(string Label, NavGroup[] Groups);

    public static readonly NavSection[] Sections =
    [
        new("OUTPATIENT / CLINIC", [
            new(null, [
                new("Dashboard", "/Dashboard/Index", ClinicalFormKeys.Dashboard),
                new("Messages", "/Messages/Index", ClinicalFormKeys.Messaging),
                new("Workflow", "/Workflow/Index", ClinicalFormKeys.Workflow),
                new("Patient Registration", "/PatientRegistration/Index", ClinicalFormKeys.PatientRegistration),
                new("Doctors", "/Doctors/Index", ClinicalFormKeys.Doctors),
                new("Prescriptions", "/Prescriptions/Index", ClinicalFormKeys.Prescriptions)
            ])
        ]),
        new("INPATIENT / WARD", [
            new(null, [
                new("Doctor Surgery", "/Surgery/Index", ClinicalFormKeys.DoctorSurgery),
                new("Book Room", "/Rooms/Book", ClinicalFormKeys.BookRoom),
                new("Patient Ward Room", "/Ward/PatientRoom", ClinicalFormKeys.PatientWardRoom),
                new("Ward Census", "/Ward/PatientReport", ClinicalFormKeys.WardPatientReport)
            ])
        ]),
        new("LABORATORY", [
            new(null, [
                new("Lab Registration", "/Laboratory/Registration", ClinicalFormKeys.LabRegistration),
                new("Lab Request", "/Laboratory/Request", ClinicalFormKeys.LabRequest),
                new("Lab Result", "/Laboratory/Result", ClinicalFormKeys.LabResult)
            ])
        ]),
        new("RADIOLOGY", [
            new(null, [
                new("Radiology Registration", "/Radiology/Registration", ClinicalFormKeys.RadiologyRegistration),
                new("Radiology Request", "/Radiology/Request", ClinicalFormKeys.RadiologyRequest),
                new("Radiology Result", "/Radiology/Result", ClinicalFormKeys.RadiologyResult)
            ])
        ]),
        new("PHARMACY", [
            new("Setup", [
                new("Item Registration", "/Pharmacy/Registration", ClinicalFormKeys.PharmacyRegistration),
                new("Opening Balance", "/Pharmacy/OpeningBalance", ClinicalFormKeys.PharmacyOpeningBalance),
                new("Purchase Bill", "/Pharmacy/Purchase", ClinicalFormKeys.PharmacyPurchaseBill)
            ]),
            new("Operations", [
                new("Pharmacy Request", "/Pharmacy/Request", ClinicalFormKeys.PharmacyRequest),
                new("Pharmacy Bill", "/Pharmacy/Bill", ClinicalFormKeys.PharmacyBill)
            ])
        ]),
        new("BILLING", [
            new("Catalog & charges", [
                new("Service Income", "/ServiceIncomes/Index", ClinicalFormKeys.ServiceIncomes),
                new("Service Income Request", "/ServiceIncomes/Request", ClinicalFormKeys.ServiceIncomeRequest),
                new("Invoices", "/Invoices/Index", ClinicalFormKeys.Invoices)
            ]),
            new("Cash & accounting", [
                new("Cash Receipt", "/CashReceipts/Index", ClinicalFormKeys.CashReceipts),
                new("Cash Payment", "/CashPayments/Index", ClinicalFormKeys.CashPayments),
                new("Expenses", "/Expenses/Index", ClinicalFormKeys.Expenses),
                new("Chart of Accounts", "/ChartOfAccounts/Index", ClinicalFormKeys.ChartAccounts)
            ])
        ]),
        new("REPORTS", [
            new("Clinical", [
                new("Patient History", "/Reports/PatientHistory", ClinicalFormKeys.PatientHistory),
                new("Patient Status", "/Reports/PatientStatus", ClinicalFormKeys.PatientStatus),
                new("Appointment Reminders", "/Reports/AppointmentReminders", ClinicalFormKeys.AppointmentReminders),
                new("Doctor Report", "/Reports/DoctorReport", ClinicalFormKeys.DoctorReport)
            ]),
            new("Operational", [
                new("Request Report", "/Reports/RequestReport", ClinicalFormKeys.RequestReport),
                new("Pharmacy Inventory", "/Reports/PharmacyInventory", ClinicalFormKeys.PharmacyInventory),
                new("Cash Report", "/Reports/CashReport", ClinicalFormKeys.CashReport)
            ]),
            new("Financial", [
                new("Accounts Receivable", "/Reports/AccountsReceivable", ClinicalFormKeys.AccountsReceivable),
                new("Accounts Payable", "/Reports/AccountsPayable", ClinicalFormKeys.AccountsPayable),
                new("PL Statement", "/Reports/PlStatement", ClinicalFormKeys.PlStatement),
                new("General Ledger", "/Reports/GeneralLedger", ClinicalFormKeys.GeneralLedger),
                new("Trial Balance", "/Reports/TrialBalance", ClinicalFormKeys.TrialBalance),
                new("Cost of Goods Sold", "/Reports/CostOfGoodsSold", ClinicalFormKeys.CostOfGoodsSold),
                new("Balance Sheet", "/Reports/BalanceSheet", ClinicalFormKeys.BalanceSheet)
            ])
        ]),
        new("CLINIC ADMIN", [
            new(null, [
                new("Role Permissions", "/Admin/Responsibilities", ClinicalFormKeys.RolePermissions),
                new("Audit Log", "/Admin/AuditLog", ClinicalFormKeys.AuditLog),
                new("Data Backup", "/Admin/Backup", ClinicalFormKeys.Backup),
                new("Data Privacy", "/Admin/DataPrivacy", ClinicalFormKeys.Settings),
                new("Settings", "/Settings/Index", ClinicalFormKeys.Settings)
            ])
        ]),
        new("PLATFORM", [
            new(null, [
                new("Enterprise", "/Admin/Enterprise", ClinicalFormKeys.Settings),
                new("Integrations", "/Admin/Integrations", ClinicalFormKeys.Settings),
                new("Subscription", "/Billing/Index", ClinicalFormKeys.Invoices)
            ])
        ])
    ];

    public static IEnumerable<(string Section, string? Group, string FormKey, string Label)> PermissionForms()
    {
        foreach (var section in Sections)
        {
            foreach (var group in section.Groups)
            {
                foreach (var link in group.Links.Where(l => l.FormKey is not null && l.RequiresPermission))
                {
                    yield return (section.Label, group.SubLabel, link.FormKey!, link.Label);
                }
            }
        }
    }

    /// <summary>One row per form key (first nav label wins) for role permission grids.</summary>
    public static IEnumerable<(string Section, string? Group, string FormKey, string Label)> DistinctPermissionForms()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in PermissionForms())
        {
            if (seen.Add(item.FormKey))
                yield return item;
        }
    }

    public static HashSet<string> AllPermissionFormKeys() =>
        PermissionForms().Select(p => p.FormKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsLinkVisible(NavLink link, HashSet<string>? visibleForms)
    {
        if (!link.RequiresPermission || link.FormKey is null)
            return true;
        return visibleForms?.Contains(link.FormKey) == true;
    }

    public static bool IsSectionVisible(NavSection section, HashSet<string>? visibleForms) =>
        section.Groups.SelectMany(g => g.Links).Any(l => IsLinkVisible(l, visibleForms));
}
