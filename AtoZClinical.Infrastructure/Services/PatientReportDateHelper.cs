using AtoZClinical.Core.Entities;

namespace AtoZClinical.Infrastructure.Services;

public static class PatientReportDateHelper
{
    private const int MinValidYear = 2000;

    public static DateTime GetEffectiveAppointmentDate(Patient patient)
    {
        if (HasValidAppointmentDate(patient))
            return ClinicClock.ToClinicDate(patient.AppointmentDate)!.Value;
        return ClinicClock.ToClinicDate(patient.CreatedAt);
    }

    public static bool IsInDateRange(Patient patient, DateTime from, DateTime to) =>
        PatientInRange(patient, from, to.AddDays(1));

    /// <summary>
    /// Patient appears when registration date OR appointment date falls in the range.
    /// </summary>
    public static bool PatientInRange(Patient patient, DateTime from, DateTime endExclusive)
    {
        var created = ClinicClock.ToClinicDate(patient.CreatedAt);
        if (created >= from && created < endExclusive)
            return true;

        if (!HasValidAppointmentDate(patient))
            return false;

        var appt = ClinicClock.ToClinicDate(patient.AppointmentDate)!.Value;
        return appt >= from && appt < endExclusive;
    }

    public static bool HasValidAppointmentDate(Patient patient) =>
        patient.AppointmentDate.HasValue &&
        ClinicClock.ToClinicDate(patient.AppointmentDate)!.Value.Year >= MinValidYear;
}
