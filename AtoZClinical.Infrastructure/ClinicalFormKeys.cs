namespace AtoZClinical.Infrastructure;

public static class ClinicalFormKeys
{
    public const string Dashboard = "Dashboard";
    public const string Workflow = "Workflow";
    public const string Doctors = "Doctors";
    public const string ServiceIncomes = "ServiceIncomes";
    public const string ServiceIncomeRequest = "ServiceIncomes.Request";
    public const string PatientRegistration = "PatientRegistration";
    public const string CashReceipts = "CashReceipts";
    public const string CashPayments = "CashPayments";
    public const string Expenses = "Expenses";
    public const string LabRegistration = "Laboratory.Registration";
    public const string LabRequest = "Laboratory.Request";
    public const string LabResult = "Laboratory.Result";
    public const string RadiologyRegistration = "Radiology.Registration";
    public const string RadiologyRequest = "Radiology.Request";
    public const string RadiologyResult = "Radiology.Result";
    public const string Prescriptions = "Prescriptions";
    public const string Invoices = "Invoices";
    public const string ChartAccounts = "ChartAccounts";
    public const string PharmacyRegistration = "Pharmacy.Registration";
    public const string PharmacyRequest = "Pharmacy.Request";
    public const string PharmacyBill = "Pharmacy.Bill";
    public const string PharmacyPurchaseBill = "Pharmacy.PurchaseBill";
    public const string PharmacyOpeningBalance = "Pharmacy.OpeningBalance";
    public const string AuditLog = "AuditLog";
    public const string RolePermissions = "RolePermissions";
    public const string PatientHistory = "Reports.PatientHistory";
    public const string AppointmentReminders = "Reports.AppointmentReminders";
    public const string PatientStatus = "Reports.PatientStatus";
    public const string PlStatement = "Reports.PlStatement";
    public const string GeneralLedger = "Reports.GeneralLedger";
    public const string TrialBalance = "Reports.TrialBalance";
    public const string CostOfGoodsSold = "Reports.CostOfGoodsSold";
    public const string BalanceSheet = "Reports.BalanceSheet";
    public const string AccountsReceivable = "Reports.AccountsReceivable";
    public const string AccountsPayable = "Reports.AccountsPayable";
    public const string OperatingReport = "Reports.OperatingReport";
    public const string CashReport = "Reports.CashReport";
    public const string PharmacyInventory = "Reports.PharmacyInventory";
    public const string DoctorReport = "Reports.DoctorReport";
    public const string RequestReport = "Reports.RequestReport";
    public const string Backup = "Admin.Backup";
    public const string Settings = "Settings";
    public const string Messaging = "Messaging";

    public static readonly string[] All =
    [
        Dashboard, Workflow, Doctors, ServiceIncomes, ServiceIncomeRequest, PatientRegistration, CashReceipts, CashPayments,
        LabRegistration, LabRequest, LabResult,
        RadiologyRegistration, RadiologyRequest, RadiologyResult,
        Prescriptions, Invoices, ChartAccounts, Expenses,
        PharmacyRegistration, PharmacyRequest, PharmacyBill, PharmacyPurchaseBill, PharmacyOpeningBalance,
        PatientHistory, AppointmentReminders, PatientStatus, PlStatement, GeneralLedger, TrialBalance, CostOfGoodsSold, BalanceSheet,
        AccountsReceivable, AccountsPayable, OperatingReport, CashReport, PharmacyInventory, DoctorReport, RequestReport,
        AuditLog, RolePermissions, Backup, Settings, Messaging
    ];
}
