using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class DashboardService
{
    private readonly ReportingDataService _reporting;

    public DashboardService(ReportingDataService reporting) => _reporting = reporting;

    public async Task<DashboardSummary> GetSummaryAsync(Guid clinicId, DateTime from, DateTime to, bool isTodayScope)
    {
        var _db = _reporting.ReadDb;
        var today = DateTime.Today;
        var rangeFrom = isTodayScope ? today : from.Date;
        var rangeTo = isTodayScope ? today : to.Date;
        if (rangeTo < rangeFrom)
            (rangeFrom, rangeTo) = (rangeTo, rangeFrom);

        var rangeEndExclusive = rangeTo.AddDays(1);
        var todayEndExclusive = today.AddDays(1);

        var activeDoctorCount = await _db.Doctors
            .AsNoTracking()
            .CountAsync(d => d.ClinicId == clinicId &&
                             d.Status != null &&
                             d.Status.ToLower() == "active");

        var todayStatusRows = await _db.Patients
            .AsNoTracking()
            .Where(p => p.ClinicId == clinicId &&
                        p.AppointmentDate >= today &&
                        p.AppointmentDate < todayEndExclusive)
            .Select(p => p.Status)
            .ToListAsync();

        var periodStatusRows = await _db.Patients
            .AsNoTracking()
            .Where(p => p.ClinicId == clinicId &&
                        p.AppointmentDate >= rangeFrom &&
                        p.AppointmentDate < rangeEndExclusive)
            .Select(p => p.Status)
            .ToListAsync();

        var newRegistrations = await _db.Patients
            .AsNoTracking()
            .CountAsync(p => p.ClinicId == clinicId &&
                             p.CreatedAt >= rangeFrom &&
                             p.CreatedAt < rangeEndExclusive);

        var invoiceTotal = await _db.Invoices
            .AsNoTracking()
            .Where(i => i.ClinicId == clinicId &&
                        i.InvoiceDate >= rangeFrom &&
                        i.InvoiceDate < rangeEndExclusive)
            .SumAsync(i => (decimal?)i.TotalAmount) ?? 0m;

        var outstandingAr = await _db.Invoices
            .AsNoTracking()
            .Where(i => i.ClinicId == clinicId)
            .SumAsync(i => (decimal?)i.BalanceDue) ?? 0m;

        var cashReceived = await _db.CashReceipts
            .AsNoTracking()
            .Where(r => r.ClinicId == clinicId &&
                        r.ReceiptDate >= rangeFrom &&
                        r.ReceiptDate < rangeEndExclusive)
            .SumAsync(r => (decimal?)r.Amount) ?? 0m;

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
