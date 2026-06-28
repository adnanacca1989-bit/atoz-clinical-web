using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;

namespace AtoZClinical.Tests;

public class PatientInvoiceServiceTests
{
    [Fact]
    public async Task GetChargesAsync_returning_patient_keeps_new_visit_balance_after_prior_payment()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        db.Db.Doctors.Add(new Doctor
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            DoctorNo = 1,
            Name = "Mohammed",
            Specialty = "Pediatrics",
            ConsultationFee = 15_000m
        });

        var priorVisitId = Guid.NewGuid();
        var newVisitId = Guid.NewGuid();
        db.Db.Patients.AddRange(
            new Patient
            {
                Id = priorVisitId,
                ClinicId = clinicId,
                PatientNo = "PAT-00003",
                FirstName = "AAA",
                DoctorName = "Mohammed",
                Specialty = "Pediatrics",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new Patient
            {
                Id = newVisitId,
                ClinicId = clinicId,
                PatientNo = "PAT-00006",
                FirstName = "AAA",
                DoctorName = "Mohammed",
                Specialty = "Pediatrics",
                CreatedAt = DateTime.UtcNow
            });
        db.Db.CashReceipts.Add(new CashReceipt
        {
            ClinicId = clinicId,
            ReceiptNo = 1,
            ReceiptDate = DateTime.Today.AddDays(-1),
            PatientId = "PAT-00003",
            PatientName = "AAA",
            DoctorName = "Mohammed",
            Amount = 15_000m,
            BalanceDue = 15_000m
        });
        await db.Db.SaveChangesAsync();

        var service = new PatientInvoiceService(db.Db);
        var summary = await service.GetChargesAsync(clinicId, "PAT-00006", "AAA", "Mohammed", newVisitId);

        Assert.Equal(15_000m, summary.SubTotal);
        Assert.Equal(0m, summary.TotalPaid);
        Assert.Equal(15_000m, summary.Balance);
    }

    [Fact]
    public void MatchesPatient_with_barcode_does_not_cross_match_prior_visit_by_name()
    {
        var matches = PatientChargeMatcher.MatchesPatient(
            "PAT-00006",
            "AAA",
            null,
            "PAT-00003",
            "AAA");

        Assert.False(matches);
    }
}
