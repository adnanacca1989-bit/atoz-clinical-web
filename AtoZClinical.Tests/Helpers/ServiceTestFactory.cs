using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtoZClinical.Tests.Helpers;

internal static class ServiceTestFactory
{
    public static MasterDataPropagationService CreatePropagation(ClinicalDbContext db)
    {
        var invoices = new PatientInvoiceService(db);
        var journalSync = new ClinicalJournalSyncService(db, NullLogger<ClinicalJournalSyncService>.Instance);
        var billing = new BillingPropagationService(db, invoices, journalSync);
        return new MasterDataPropagationService(db, billing, journalSync);
    }

    public static InvoiceService CreateInvoiceService(ClinicalDbContext db)
    {
        var audit = new AuditService(db);
        var invoices = new PatientInvoiceService(db);
        var journalSync = new ClinicalJournalSyncService(db, NullLogger<ClinicalJournalSyncService>.Instance);
        var demographics = new ClinicalDemographicsSyncService(db);
        var visitStatus = new PatientVisitStatusService(db, audit);
        return new InvoiceService(
            db,
            audit,
            visitStatus,
            journalSync,
            invoices,
            demographics);
    }

    public static PatientService CreatePatientService(ClinicalDbContext db)
    {
        var audit = new AuditService(db);
        return new PatientService(
            db,
            CreatePropagation(db),
            new InvoiceDeleteGuardService(db),
            new PatientVisitStatusService(db, audit),
            new NoOpWebhookDispatchService(),
            audit,
            new ClinicalDemographicsSyncService(db),
            CreateInvoiceService(db));
    }
}
