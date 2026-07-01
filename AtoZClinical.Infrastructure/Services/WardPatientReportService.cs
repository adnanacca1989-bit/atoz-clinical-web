using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class WardPatientReportService
{
    private readonly ClinicalDbContext _db;

    public WardPatientReportService(ClinicalDbContext db) => _db = db;

    public async Task<WardPatientReportResult> GetRowsAsync(
        Guid clinicId,
        DateTime fromDate,
        DateTime toDate,
        string? patientBarcode,
        string? patientName,
        string? doctorName)
    {
        var from = fromDate.Date;
        var to = toDate.Date;
        if (from > to)
            (from, to) = (to, from);

        var patients = await _db.Patients.ForClinic(clinicId).AsNoTracking().ToListAsync();
        var resolvedPatient = ResolveFilterPatient(patients, patientBarcode, patientName);
        var hasPatientFilter = resolvedPatient is not null
            || !string.IsNullOrWhiteSpace(patientBarcode)
            || !string.IsNullOrWhiteSpace(patientName);

        var bookings = await _db.RoomBookings.ForClinic(clinicId).AsNoTracking().ToListAsync();

        if (hasPatientFilter)
        {
            bookings = bookings
                .Where(b => MatchesPatientFilter(b, resolvedPatient, patientBarcode, patientName))
                .ToList();
        }
        else
        {
            bookings = bookings
                .Where(b => IsInReportDateRange(b, from, to))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(doctorName))
        {
            var doc = doctorName.Trim();
            bookings = bookings.Where(b =>
                b.DoctorName?.Contains(doc, StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        bookings = bookings
            .OrderBy(b => b.EnterDate ?? b.DateBook)
            .ThenBy(b => b.BookingNo)
            .ToList();

        var doctors = await _db.Doctors.ForClinic(clinicId).AsNoTracking().ToListAsync();
        var demographics = new ClinicalDemographicsSyncService(_db);

        var allInvoices = await _db.Invoices.ForClinic(clinicId).AsNoTracking().ToListAsync();
        var periodInvoices = allInvoices
            .Where(i => i.InvoiceDate.Date >= from && i.InvoiceDate.Date <= to)
            .ToList();

        var allReceipts = await _db.CashReceipts.ForClinic(clinicId).AsNoTracking()
            .Where(r => r.ReceiptDate.Date <= to)
            .ToListAsync();
        var periodReceipts = allReceipts.Where(r => r.ReceiptDate.Date >= from).ToList();

        var allPayments = await _db.CashPayments.ForClinic(clinicId).AsNoTracking()
            .Where(p => p.PaymentDate.Date <= to)
            .ToListAsync();
        var periodPayments = allPayments.Where(p => p.PaymentDate.Date >= from).ToList();

        var surgeries = await _db.DoctorSurgeries.ForClinic(clinicId).AsNoTracking().ToListAsync();
        var surgeryById = surgeries.ToDictionary(s => s.Id);

        var rows = new List<WardPatientReportRow>();
        foreach (var booking in bookings)
        {
            var livePatient = demographics.ResolvePatientFromList(
                patients, booking.PatientRecordId, booking.PatientBarcode, booking.PatientName);
            var liveDoctor = demographics.ResolveDoctorFromList(doctors, null, booking.DoctorName);
            var surgery = ResolveSurgery(booking, surgeries, surgeryById);

            var displayPatient = livePatient?.FullName ?? booking.PatientName ?? "";
            var displayAge = livePatient?.AgeYears ?? booking.Age;
            var displayCity = livePatient?.City ?? booking.City ?? "";
            var displayMother = livePatient?.MotherName ?? booking.MotherName ?? "";
            var displayDoctor = liveDoctor?.Name ?? booking.DoctorName ?? "";
            var displaySpecialty = liveDoctor?.Specialty ?? booking.Specialty ?? "";
            var displayNationalId = livePatient?.NationalId ?? booking.NationalId ?? surgery?.NationalId ?? "";
            var surgeryDate = surgery?.SurgeryDate;
            var surgeryTime = surgery?.SurgeryTime;
            var typeOfSurgery = surgery?.TypeOfSurgery ?? booking.TypeOfSurgery ?? "";
            var classify = surgery?.Classify ?? booking.Classify ?? "";
            var initialAmount = surgery?.InitialAmount ?? 0m;

            var matchedPeriodInvoices = periodInvoices
                .Where(i => MatchesBooking(booking, i))
                .ToList();
            var matchedAllInvoices = allInvoices
                .Where(i => MatchesBooking(booking, i))
                .ToList();

            var invoiceAmount = matchedPeriodInvoices.Sum(i => i.TotalAmount);
            var discount = matchedPeriodInvoices.Sum(i => i.Discount);

            var cashReceipt = periodReceipts
                .Where(r => MatchesBookingReceipt(booking, r))
                .Sum(r => r.Amount);

            var cashPayment = periodPayments
                .Where(p => MatchesBookingPayment(booking, p))
                .Sum(p => p.Amount);

            var anchorInvoice = matchedAllInvoices
                .OrderByDescending(i => i.InvoiceDate)
                .ThenByDescending(i => i.InvoiceNo)
                .FirstOrDefault();

            var endingBalance = anchorInvoice is null
                ? 0m
                : InvoiceArCalculator.ComputeGroupNetBalance(
                    anchorInvoice, allInvoices, allReceipts, allPayments);

            rows.Add(new WardPatientReportRow(
                booking.BookingNo,
                booking.PatientBarcode ?? livePatient?.PatientNo ?? "",
                displayPatient,
                displayAge,
                displayCity,
                displayMother,
                displayNationalId,
                displayDoctor,
                displaySpecialty,
                surgeryDate,
                surgeryTime,
                typeOfSurgery,
                classify,
                initialAmount,
                booking.RoomNumber,
                booking.EnterDate,
                booking.ExitDate,
                booking.EnterTime,
                booking.ExitTime,
                booking.Days ?? RoomBookingService.ComputeDays(booking.EnterDate, booking.ExitDate),
                booking.Note ?? "",
                cashReceipt,
                cashPayment,
                invoiceAmount,
                discount,
                endingBalance));
        }

        return new WardPatientReportResult(
            rows,
            rows.Sum(r => r.CashReceipt),
            rows.Sum(r => r.CashPayment),
            rows.Sum(r => r.InvoiceAmount),
            rows.Sum(r => r.Discount),
            rows.Sum(r => r.InitialAmount),
            rows.Sum(r => r.EndingBalance));
    }

    private static DoctorSurgery? ResolveSurgery(
        RoomBooking booking,
        IReadOnlyList<DoctorSurgery> surgeries,
        IReadOnlyDictionary<Guid, DoctorSurgery> surgeryById)
    {
        if (booking.DoctorSurgeryId is Guid surgeryId && surgeryById.TryGetValue(surgeryId, out var linked))
            return linked;

        if (booking.PatientRecordId is not Guid patientId)
            return null;

        return surgeries
            .Where(s => s.PatientRecordId == patientId)
            .OrderByDescending(s => s.SurgeryDate)
            .ThenByDescending(s => s.SurgeryNo)
            .FirstOrDefault();
    }

    private static bool IsInReportDateRange(RoomBooking booking, DateTime from, DateTime to)
    {
        var stayStart = (booking.EnterDate ?? booking.DateBook).Date;
        if (!booking.ExitDate.HasValue)
            return stayStart <= to;

        var stayEnd = booking.ExitDate.Value.Date;
        return stayStart <= to && stayEnd >= from;
    }

    private static Patient? ResolveFilterPatient(
        IReadOnlyList<Patient> patients,
        string? patientBarcode,
        string? patientName)
    {
        if (!string.IsNullOrWhiteSpace(patientBarcode))
        {
            var bc = patientBarcode.Trim();
            var byBarcode = patients.FirstOrDefault(p =>
                string.Equals(p.PatientNo, bc, StringComparison.OrdinalIgnoreCase) ||
                (p.NationalId != null && p.NationalId.Contains(bc, StringComparison.OrdinalIgnoreCase)));
            if (byBarcode is not null)
                return byBarcode;
        }

        if (string.IsNullOrWhiteSpace(patientName))
            return null;

        var name = patientName.Trim();
        return patients.FirstOrDefault(p =>
            p.FullName.Contains(name, StringComparison.OrdinalIgnoreCase) ||
            name.Contains(p.FullName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesPatientFilter(
        RoomBooking booking,
        Patient? resolvedPatient,
        string? patientBarcode,
        string? patientName)
    {
        if (resolvedPatient is not null && booking.PatientRecordId == resolvedPatient.Id)
            return true;

        return PatientChargeMatcher.MatchesPatient(
            patientBarcode,
            patientName,
            booking.PatientBarcode,
            booking.PatientBarcode,
            booking.PatientName,
            resolvedPatient?.Id,
            booking.PatientRecordId);
    }

    private static bool MatchesBooking(RoomBooking booking, Invoice invoice) =>
        PatientChargeMatcher.MatchesPatient(
            booking.PatientBarcode,
            booking.PatientName,
            invoice.PatientId,
            invoice.PatientId,
            invoice.PatientName,
            booking.PatientRecordId,
            invoice.PatientRecordId)
        && PatientChargeMatcher.MatchesDoctor(booking.DoctorName, invoice.DoctorName);

    private static bool MatchesBookingReceipt(RoomBooking booking, CashReceipt receipt) =>
        PatientChargeMatcher.MatchesPatient(
            booking.PatientBarcode,
            booking.PatientName,
            receipt.PatientId,
            receipt.PatientId,
            receipt.PatientName,
            booking.PatientRecordId,
            receipt.PatientRecordId)
        && PatientChargeMatcher.MatchesDoctor(booking.DoctorName, receipt.DoctorName);

    private static bool MatchesBookingPayment(RoomBooking booking, CashPayment payment) =>
        PatientChargeMatcher.MatchesPatient(
            booking.PatientBarcode,
            booking.PatientName,
            payment.PatientId,
            payment.PatientId,
            payment.PayeeName,
            booking.PatientRecordId,
            payment.PatientRecordId)
        && PatientChargeMatcher.MatchesDoctor(booking.DoctorName, payment.DoctorName);

    public sealed record WardPatientReportRow(
        int Id,
        string PatientBarcode,
        string PatientName,
        int? Age,
        string City,
        string MotherName,
        string NationalId,
        string DoctorName,
        string Specialty,
        DateTime? SurgeryDate,
        TimeSpan? SurgeryTime,
        string TypeOfSurgery,
        string Classify,
        decimal InitialAmount,
        int? RoomNumber,
        DateTime? EnterDate,
        DateTime? ExitDate,
        TimeSpan? EnterTime,
        TimeSpan? ExitTime,
        int? Days,
        string Note,
        decimal CashReceipt,
        decimal CashPayment,
        decimal InvoiceAmount,
        decimal Discount,
        decimal EndingBalance);

    public sealed record WardPatientReportResult(
        IReadOnlyList<WardPatientReportRow> Rows,
        decimal TotalCashReceipt,
        decimal TotalCashPayment,
        decimal TotalInvoiceAmount,
        decimal TotalDiscount,
        decimal TotalInitialAmount,
        decimal TotalEndingBalance);
}
