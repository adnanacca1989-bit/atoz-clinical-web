using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtoZClinical.Tests.Helpers;

internal static class ServiceTestFactory
{
    public static MasterDataPropagationService CreatePropagation(ClinicalDbContext db)
    {
        var invoices = new PatientInvoiceService(db);
        var billing = new BillingPropagationService(db, invoices);
        var journalSync = new ClinicalJournalSyncService(db, NullLogger<ClinicalJournalSyncService>.Instance);
        return new MasterDataPropagationService(db, billing, journalSync);
    }
}
