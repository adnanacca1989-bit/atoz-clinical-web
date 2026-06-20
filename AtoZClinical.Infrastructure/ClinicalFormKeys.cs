namespace AtoZClinical.Infrastructure;

public static class ClinicalFormKeys
{
    public const string Dashboard = "Dashboard";
    public const string Doctors = "Doctors";
    public const string ServiceIncomes = "ServiceIncomes";
    public const string PatientRegistration = "PatientRegistration";
    public const string CashReceipts = "CashReceipts";
    public const string CashPayments = "CashPayments";
    public const string LabRegistration = "Laboratory.Registration";
    public const string LabRequest = "Laboratory.Request";
    public const string LabResult = "Laboratory.Result";
    public const string RadiologyRegistration = "Radiology.Registration";
    public const string RadiologyRequest = "Radiology.Request";
    public const string RadiologyResult = "Radiology.Result";
    public const string Prescriptions = "Prescriptions";
    public const string Invoices = "Invoices";
    public const string ChartAccounts = "ChartAccounts";
    public const string AuditLog = "AuditLog";
    public const string RolePermissions = "RolePermissions";
    public const string PatientHistory = "Reports.PatientHistory";
    public const string PatientStatus = "Reports.PatientStatus";
    public const string PlStatement = "Reports.PlStatement";
    public const string AccountsReceivable = "Reports.AccountsReceivable";
    public const string OperatingReport = "Reports.OperatingReport";
    public const string CashReport = "Reports.CashReport";
    public const string PharmacyRequest = "Pharmacy.Request";
    public const string PharmacyBill = "Pharmacy.Bill";
    public const string PharmacyPurchaseBill = "Pharmacy.PurchaseBill";
    public const string PharmacyRegistration = "Pharmacy.Registration";
    public const string PharmacyOpeningBalance = "Pharmacy.OpeningBalance";
    public const string PharmacyInventory = "Reports.PharmacyInventory";
    public const string Backup = "Admin.Backup";

    public static readonly string[] All =
    [
        Dashboard, Doctors, ServiceIncomes, PatientRegistration, CashReceipts, CashPayments,
        LabRegistration, LabRequest, LabResult,
        RadiologyRegistration, RadiologyRequest, RadiologyResult,
        Prescriptions, Invoices, ChartAccounts,
        PharmacyRegistration, PharmacyRequest, PharmacyBill, PharmacyPurchaseBill, PharmacyOpeningBalance,
        PatientHistory, PatientStatus, PlStatement, AccountsReceivable, OperatingReport, CashReport, PharmacyInventory,
        AuditLog, RolePermissions, Backup
    ];
}
