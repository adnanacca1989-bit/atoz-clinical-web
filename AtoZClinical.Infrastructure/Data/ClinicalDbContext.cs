using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Data;

public class ClinicalDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly Guid? _tenantClinicId;
    private readonly bool _bypassTenantFilter;

    public ClinicalDbContext(DbContextOptions<ClinicalDbContext> options)
        : this(options, BypassClinicProvider.Instance)
    {
    }

    public ClinicalDbContext(DbContextOptions<ClinicalDbContext> options, ICurrentClinicProvider tenant)
        : base(options)
    {
        _bypassTenantFilter = tenant.BypassTenantFilter;
        _tenantClinicId = tenant.ClinicId;
    }

    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<ServiceIncome> ServiceIncomes => Set<ServiceIncome>();
    public DbSet<ServiceIncomeRequest> ServiceIncomeRequests => Set<ServiceIncomeRequest>();
    public DbSet<ServiceIncomeRequestLine> ServiceIncomeRequestLines => Set<ServiceIncomeRequestLine>();
    public DbSet<CashReceipt> CashReceipts => Set<CashReceipt>();
    public DbSet<LabTest> LabTests => Set<LabTest>();
    public DbSet<LabRequest> LabRequests => Set<LabRequest>();
    public DbSet<LabRequestLine> LabRequestLines => Set<LabRequestLine>();
    public DbSet<LabResult> LabResults => Set<LabResult>();
    public DbSet<LabResultLine> LabResultLines => Set<LabResultLine>();
    public DbSet<RadiologyTest> RadiologyTests => Set<RadiologyTest>();
    public DbSet<RadiologyRequest> RadiologyRequests => Set<RadiologyRequest>();
    public DbSet<RadiologyRequestLine> RadiologyRequestLines => Set<RadiologyRequestLine>();
    public DbSet<RadiologyResult> RadiologyResults => Set<RadiologyResult>();
    public DbSet<RadiologyResultLine> RadiologyResultLines => Set<RadiologyResultLine>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionLine> PrescriptionLines => Set<PrescriptionLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<CashPayment> CashPayments => Set<CashPayment>();
    public DbSet<ExpenseVoucher> ExpenseVouchers => Set<ExpenseVoucher>();
    public DbSet<ExpenseVoucherLine> ExpenseVoucherLines => Set<ExpenseVoucherLine>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<ChartAccount> ChartAccounts => Set<ChartAccount>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<PharmacyRequest> PharmacyRequests => Set<PharmacyRequest>();
    public DbSet<PharmacyRequestLine> PharmacyRequestLines => Set<PharmacyRequestLine>();
    public DbSet<PharmacyBill> PharmacyBills => Set<PharmacyBill>();
    public DbSet<PharmacyBillLine> PharmacyBillLines => Set<PharmacyBillLine>();
    public DbSet<PharmacyItem> PharmacyItems => Set<PharmacyItem>();
    public DbSet<PharmacyOpeningBalance> PharmacyOpeningBalances => Set<PharmacyOpeningBalance>();
    public DbSet<PharmacyOpeningBalanceLine> PharmacyOpeningBalanceLines => Set<PharmacyOpeningBalanceLine>();
    public DbSet<PharmacyInventoryMovement> PharmacyInventoryMovements => Set<PharmacyInventoryMovement>();
    public DbSet<ClinicConfiguration> ClinicConfigurations => Set<ClinicConfiguration>();
    public DbSet<ClinicUom> ClinicUoms => Set<ClinicUom>();
    public DbSet<ClinicCurrency> ClinicCurrencies => Set<ClinicCurrency>();
    public DbSet<ClinicOwner> ClinicOwners => Set<ClinicOwner>();
    public DbSet<ClinicLanguage> ClinicLanguages => Set<ClinicLanguage>();
    public DbSet<ClinicVendor> ClinicVendors => Set<ClinicVendor>();
    public DbSet<PharmacyPurchaseBill> PharmacyPurchaseBills => Set<PharmacyPurchaseBill>();
    public DbSet<PharmacyPurchaseBillLine> PharmacyPurchaseBillLines => Set<PharmacyPurchaseBillLine>();
    public DbSet<ClinicApiKey> ClinicApiKeys => Set<ClinicApiKey>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<SecurityAuditEntry> SecurityAuditEntries => Set<SecurityAuditEntry>();
    public DbSet<ClinicBackupHistory> ClinicBackupHistories => Set<ClinicBackupHistory>();
    public DbSet<ClinicMessage> ClinicMessages => Set<ClinicMessage>();
    public DbSet<ClinicMessageAttachment> ClinicMessageAttachments => Set<ClinicMessageAttachment>();
    public DbSet<ClinicalNotification> ClinicalNotifications => Set<ClinicalNotification>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<RegistrationVerificationCode> RegistrationVerificationCodes => Set<RegistrationVerificationCode>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Clinic>(e =>
        {
            e.HasIndex(x => x.ClinicCode).IsUnique();
            e.HasIndex(x => x.LicenseKey).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ClinicCode).HasMaxLength(32).IsRequired();
            e.Property(x => x.PlanName).HasMaxLength(64);
            e.Property(x => x.StripeCustomerId).HasMaxLength(128);
            e.Property(x => x.StripeSubscriptionId).HasMaxLength(128);
            e.Property(x => x.SubscriptionStatus).HasMaxLength(32);
            e.Property(x => x.SubscriptionType).HasMaxLength(64);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.SubscriptionExpiryDate);
            e.HasIndex(x => x.PlanName);
            e.HasIndex(x => x.StripeCustomerId);
            e.HasIndex(x => x.StripeSubscriptionId);
            e.Property(x => x.Subdomain).HasMaxLength(63);
            e.HasIndex(x => x.Subdomain).IsUnique().HasFilter("\"Subdomain\" IS NOT NULL");
            e.Property(x => x.DedicatedConnectionName).HasMaxLength(128);
        });

        builder.Entity<Patient>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.PatientNo }).IsUnique();
            e.HasIndex(x => new { x.ClinicId, x.AppointmentDate });
            e.HasIndex(x => new { x.ClinicId, x.Status });
            e.Property(x => x.FirstName).HasMaxLength(150).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(150);
            e.HasOne(x => x.Clinic).WithMany(c => c.Patients).HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Appointment>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.AppointmentDate });
            e.HasOne(x => x.Clinic).WithMany(c => c.Appointments).HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Patient).WithMany(p => p.Appointments).HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Doctor>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.DoctorNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ServiceIncome>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.ServiceNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ServiceIncomeRequest>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.RequestNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ServiceIncomeRequestLine>(e =>
        {
            e.HasOne(x => x.ServiceIncomeRequest).WithMany(r => r.Lines).HasForeignKey(x => x.ServiceIncomeRequestId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ClinicMessage>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.SenderUserId, x.RecipientUserId, x.SentAt });
            e.HasIndex(x => new { x.ClinicId, x.RecipientUserId, x.ReadAt });
            e.Property(x => x.SenderUserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.RecipientUserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.Body).HasMaxLength(4000);
            e.HasOne(x => x.Attachment).WithMany().HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ClinicMessageAttachment>(e =>
        {
            e.Property(x => x.UploadedByUserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        });

        builder.Entity<ClinicalNotification>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.CreatedAt });
            e.HasIndex(x => new { x.ClinicId, x.TargetRole });
            e.Property(x => x.TargetRole).HasMaxLength(64).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Detail).HasMaxLength(500);
            e.Property(x => x.Link).HasMaxLength(256);
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CashReceipt>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.ReceiptNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LabTest>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.TestNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LabRequest>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.RequestNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LabRequestLine>(e =>
        {
            e.HasOne(x => x.LabRequest).WithMany(r => r.Lines).HasForeignKey(x => x.LabRequestId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LabResult>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.ResultNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LabResultLine>(e =>
        {
            e.HasOne(x => x.LabResult).WithMany(r => r.Lines).HasForeignKey(x => x.LabResultId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RadiologyTest>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.TestNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RadiologyRequest>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.RequestNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RadiologyRequestLine>(e =>
        {
            e.HasOne(x => x.RadiologyRequest).WithMany(r => r.Lines).HasForeignKey(x => x.RadiologyRequestId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RadiologyResult>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.ResultNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RadiologyResultLine>(e =>
        {
            e.HasOne(x => x.RadiologyResult).WithMany(r => r.Lines).HasForeignKey(x => x.RadiologyResultId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Prescription>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.PrescriptionNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PrescriptionLine>(e =>
        {
            e.HasOne(x => x.Prescription).WithMany(p => p.Lines).HasForeignKey(x => x.PrescriptionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Invoice>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.InvoiceNo }).IsUnique();
            e.HasIndex(x => new { x.ClinicId, x.InvoiceDate });
            e.HasIndex(x => new { x.ClinicId, x.PatientId });
            e.HasIndex(x => new { x.ClinicId, x.PatientName });
            e.HasIndex(x => new { x.ClinicId, x.PatientRecordId });
            e.HasIndex(x => new { x.ClinicId, x.DoctorRecordId });
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<InvoiceLine>(e =>
        {
            e.HasOne(x => x.Invoice).WithMany(i => i.Lines).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CashPayment>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.PaymentNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChartAccount>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.AccountNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ExpenseVoucher>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.ExpenseNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ExpenseVoucherLine>(e =>
        {
            e.HasOne(x => x.ExpenseVoucher).WithMany(v => v.Lines).HasForeignKey(x => x.ExpenseVoucherId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<JournalEntry>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.EntryNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<JournalEntryLine>(e =>
        {
            e.HasOne(x => x.JournalEntry).WithMany(j => j.Lines).HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AuditLogEntry>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.DateTime });
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RolePermission>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.RoleName, x.FormKey }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PharmacyRequest>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.RequestNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PharmacyRequestLine>(e =>
        {
            e.HasOne(x => x.PharmacyRequest).WithMany(r => r.Lines).HasForeignKey(x => x.PharmacyRequestId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PharmacyBill>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.BillNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PharmacyBillLine>(e =>
        {
            e.HasOne(x => x.PharmacyBill).WithMany(b => b.Lines).HasForeignKey(x => x.PharmacyBillId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PharmacyItem>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.Barcode }).IsUnique();
            e.HasIndex(x => new { x.ClinicId, x.ItemNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PharmacyOpeningBalance>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.BalanceNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PharmacyOpeningBalanceLine>(e =>
        {
            e.HasOne(x => x.PharmacyOpeningBalance).WithMany(b => b.Lines).HasForeignKey(x => x.PharmacyOpeningBalanceId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PharmacyInventoryMovement>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.ReferenceType, x.ReferenceId });
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PharmacyItem).WithMany(i => i.Movements).HasForeignKey(x => x.PharmacyItemId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ClinicConfiguration>(e =>
        {
            e.HasIndex(x => x.ClinicId).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.CurrencyCode).HasMaxLength(8);
            e.Property(x => x.CurrencySymbol).HasMaxLength(8);
            e.Property(x => x.LanguageCode).HasMaxLength(8);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
            e.Property(x => x.Tagline).HasMaxLength(200);
            e.Property(x => x.Website).HasMaxLength(256);
        });

        builder.Entity<SecurityAuditEntry>(e =>
        {
            e.HasIndex(x => x.ClinicId);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.EventType);
            e.Property(x => x.EventType).HasMaxLength(64).IsRequired();
            e.Property(x => x.UserName).HasMaxLength(256);
            e.Property(x => x.IpAddress).HasMaxLength(64);
        });

        builder.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("PasswordResetTokens");
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        });

        builder.Entity<RegistrationVerificationCode>(e =>
        {
            e.ToTable("RegistrationVerificationCodes");
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.CodeHash);
            e.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.Destination).HasMaxLength(256).IsRequired();
            e.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
        });

        builder.Entity<ClinicBackupHistory>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.CreatedAt });
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Action).HasMaxLength(32).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(256).IsRequired();
        });

        builder.Entity<ClinicUom>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.UomNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Code).HasMaxLength(32).IsRequired();
        });

        builder.Entity<ClinicCurrency>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.CurrencyNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Code).HasMaxLength(8).IsRequired();
        });

        builder.Entity<ClinicOwner>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.OwnerNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        builder.Entity<ClinicLanguage>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.LanguageNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Code).HasMaxLength(8).IsRequired();
        });

        builder.Entity<ClinicVendor>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.VendorNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        builder.Entity<PharmacyPurchaseBill>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.PurchaseNo }).IsUnique();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PharmacyPurchaseBillLine>(e =>
        {
            e.HasOne(x => x.PharmacyPurchaseBill).WithMany(b => b.Lines).HasForeignKey(x => x.PharmacyPurchaseBillId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ClinicApiKey>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.Name });
            e.HasIndex(x => x.KeyPrefix);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.KeyPrefix).HasMaxLength(16).IsRequired();
            e.Property(x => x.KeyHash).HasMaxLength(128).IsRequired();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WebhookSubscription>(e =>
        {
            e.HasIndex(x => new { x.ClinicId, x.TargetUrl });
            e.Property(x => x.TargetUrl).HasMaxLength(500).IsRequired();
            e.Property(x => x.Secret).HasMaxLength(128).IsRequired();
            e.Property(x => x.Events).HasMaxLength(500).IsRequired();
            e.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.DoctorRecordId);
        });

        ApplyClinicTenantFilters(builder);
    }

    private void ApplyClinicTenantFilters(ModelBuilder builder)
    {
        ConfigureClinicFilter<Patient>(builder);
        ConfigureClinicFilter<Appointment>(builder);
        ConfigureClinicFilter<Doctor>(builder);
        ConfigureClinicFilter<ServiceIncome>(builder);
        ConfigureClinicFilter<ServiceIncomeRequest>(builder);
        ConfigureClinicFilter<CashReceipt>(builder);
        ConfigureClinicFilter<CashPayment>(builder);
        ConfigureClinicFilter<ExpenseVoucher>(builder);
        ConfigureClinicFilter<JournalEntry>(builder);
        ConfigureClinicFilter<LabTest>(builder);
        ConfigureClinicFilter<LabRequest>(builder);
        ConfigureClinicFilter<LabResult>(builder);
        ConfigureClinicFilter<RadiologyTest>(builder);
        ConfigureClinicFilter<RadiologyRequest>(builder);
        ConfigureClinicFilter<RadiologyResult>(builder);
        ConfigureClinicFilter<Prescription>(builder);
        ConfigureClinicFilter<Invoice>(builder);
        ConfigureClinicFilter<ChartAccount>(builder);
        ConfigureClinicFilter<AuditLogEntry>(builder);
        ConfigureClinicFilter<RolePermission>(builder);
        ConfigureClinicFilter<PharmacyRequest>(builder);
        ConfigureClinicFilter<PharmacyBill>(builder);
        ConfigureClinicFilter<PharmacyItem>(builder);
        ConfigureClinicFilter<PharmacyOpeningBalance>(builder);
        ConfigureClinicFilter<PharmacyInventoryMovement>(builder);
        ConfigureClinicFilter<ClinicBackupHistory>(builder);
        ConfigureClinicFilter<ClinicConfiguration>(builder);
        ConfigureClinicFilter<ClinicUom>(builder);
        ConfigureClinicFilter<ClinicCurrency>(builder);
        ConfigureClinicFilter<ClinicOwner>(builder);
        ConfigureClinicFilter<ClinicLanguage>(builder);
        ConfigureClinicFilter<ClinicVendor>(builder);
        ConfigureClinicFilter<PharmacyPurchaseBill>(builder);
        ConfigureClinicFilter<ClinicApiKey>(builder);
        ConfigureClinicFilter<WebhookSubscription>(builder);
        ConfigureClinicFilter<ClinicMessage>(builder);
        ConfigureClinicFilter<ClinicMessageAttachment>(builder);
    }

    private void ConfigureClinicFilter<TEntity>(ModelBuilder builder) where TEntity : class, IClinicScoped
    {
        builder.Entity<TEntity>().HasQueryFilter(e =>
            _bypassTenantFilter || (_tenantClinicId != null && e.ClinicId == _tenantClinicId));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PreventAuditMutation();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PreventAuditMutation();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PreventAuditMutation()
    {
        foreach (var entry in ChangeTracker.Entries<AuditLogEntry>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                entry.State = EntityState.Unchanged;
        }
    }
}
