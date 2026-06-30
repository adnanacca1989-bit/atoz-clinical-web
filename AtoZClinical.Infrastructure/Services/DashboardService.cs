using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class DashboardService
{
    private readonly ReportingDataService _reporting;
    private readonly PatientVisitStatusService _visitStatus;
    private readonly DoctorScopeContext _doctorScope;

    public DashboardService(
        ReportingDataService reporting,
        PatientVisitStatusService visitStatus,
        DoctorScopeContext doctorScope)
    {
        _reporting = reporting;
        _visitStatus = visitStatus;
        _doctorScope = doctorScope;
    }

    public async Task<DashboardSummary> GetSummaryAsync(Guid clinicId, DateTime from, DateTime to, bool isTodayScope)
    {
        await _visitStatus.SyncAllPatientStatusesForClinicAsync(clinicId);

        var db = _reporting.ReadDb;
        var scope = _doctorScope.Filter;
        var today = DateTime.Today;
        var rangeFrom = isTodayScope ? today : from.Date;
        var rangeTo = isTodayScope ? today : to.Date;
        if (rangeTo < rangeFrom)
            (rangeFrom, rangeTo) = (rangeTo, rangeFrom);

        var rangeEndExclusive = rangeTo.AddDays(1);
        var todayEndExclusive = today.AddDays(1);

        var doctorStatuses = await db.Doctors
            .ForClinic(clinicId)
            .AsNoTracking()
            .Select(d => d.Status)
            .ToListAsync();

        var activeDoctorCount = doctorStatuses.Count(status =>
            string.IsNullOrWhiteSpace(status) ||
            !status.Equals("inactive", StringComparison.OrdinalIgnoreCase));

        var scopedPatients = db.Patients.ForClinic(clinicId).AsNoTracking().Apply(scope);

        var todayStatusRows = await PatientsInRange(scopedPatients, today, todayEndExclusive)
            .Select(p => p.Status)
            .ToListAsync();

        var periodStatusRows = await PatientsInRange(scopedPatients, rangeFrom, rangeEndExclusive)
            .Select(p => p.Status)
            .ToListAsync();

        var newRegistrations = await scopedPatients
            .CountAsync(p => p.CreatedAt >= rangeFrom && p.CreatedAt < rangeEndExclusive);

        var invoiceTotal = await db.Invoices
            .ForClinic(clinicId)
            .AsNoTracking()
            .Apply(scope)
            .Where(i => i.InvoiceDate >= rangeFrom && i.InvoiceDate < rangeEndExclusive)
            .SumNullableAsync(db, i => i.TotalAmount);

        var outstandingAr = await db.Invoices
            .ForClinic(clinicId)
            .AsNoTracking()
            .Apply(scope)
            .SumNullableAsync(db, i => i.BalanceDue);

        var cashReceived = await db.CashReceipts
            .ForClinic(clinicId)
            .AsNoTracking()
            .Apply(scope)
            .Where(r => r.ReceiptDate >= rangeFrom && r.ReceiptDate < rangeEndExclusive)
            .SumNullableAsync(db, r => r.Amount);

        return new DashboardSummary
        {
            ActiveDoctorCount = activeDoctorCount,
            NewRegistrations = newRegistrations,
            TodayPending = CountStatus(todayStatusRows, PatientVisitStatuses.Pending),
            TodayUnderProcess = CountStatus(todayStatusRows, PatientVisitStatuses.UnderProcess),
            TodayCompleted = CountStatus(todayStatusRows, PatientVisitStatuses.Completed),
            TodayConfirmed = CountStatus(todayStatusRows, PatientVisitStatuses.Confirmed),
            PeriodPending = CountStatus(periodStatusRows, PatientVisitStatuses.Pending),
            PeriodCancelled = CountStatus(periodStatusRows, PatientVisitStatuses.Cancelled),
            PeriodConfirmed = CountStatus(periodStatusRows, PatientVisitStatuses.Confirmed),
            PeriodUnderProcess = CountStatus(periodStatusRows, PatientVisitStatuses.UnderProcess),
            PeriodCompleted = CountStatus(periodStatusRows, PatientVisitStatuses.Completed),
            InvoiceTotal = invoiceTotal,
            CashReceived = cashReceived,
            OutstandingAr = outstandingAr
        };
    }

    private static IQueryable<Patient> PatientsInRange(IQueryable<Patient> query, DateTime from, DateTime endExclusive) =>
        query.Where(p =>
            (p.AppointmentDate.HasValue &&
             p.AppointmentDate.Value >= from &&
             p.AppointmentDate.Value < endExclusive) ||
            (!p.AppointmentDate.HasValue &&
             p.CreatedAt >= from &&
             p.CreatedAt < endExclusive));

    private static int CountStatus(IEnumerable<string?> statuses, string status) =>
        statuses.Count(value => PatientVisitStatuses.Normalize(value)
            .Equals(status, StringComparison.OrdinalIgnoreCase));
}

public sealed class DashboardSummary
{
    public int ActiveDoctorCount { get; init; }
    public int NewRegistrations { get; init; }
    public int TodayPending { get; init; }
    public int TodayUnderProcess { get; init; }
    public int TodayCompleted { get; init; }
    public int TodayConfirmed { get; init; }
    public int PeriodPending { get; init; }
    public int PeriodCancelled { get; init; }
    public int PeriodConfirmed { get; init; }
    public int PeriodUnderProcess { get; init; }
    public int PeriodCompleted { get; init; }
    public decimal InvoiceTotal { get; init; }
    public decimal CashReceived { get; init; }
    public decimal OutstandingAr { get; init; }
}
