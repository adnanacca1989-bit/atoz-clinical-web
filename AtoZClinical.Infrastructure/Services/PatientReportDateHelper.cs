using AtoZClinical.Core.Entities;

namespace AtoZClinical.Infrastructure.Services;

public static class PatientReportDateHelper
{
    public static DateTime GetEffectiveAppointmentDate(Patient patient) =>
        ClinicClock.ToClinicDate(patient.AppointmentDate)
        ?? ClinicClock.ToClinicDate(patient.CreatedAt);

    public static bool IsInDateRange(Patient patient, DateTime from, DateTime to) =>
        PatientInRange(patient, from, to.AddDays(1));

    public static bool PatientInRange(Patient patient, DateTime from, DateTime endExclusive)
    {
        if (patient.AppointmentDate.HasValue)
        {
            var appt = ClinicClock.ToClinicDate(patient.AppointmentDate)!.Value;
            return appt >= from && appt < endExclusive;
        }

        var created = ClinicClock.ToClinicDate(patient.CreatedAt);
        return created >= from && created < endExclusive;
    }
}
