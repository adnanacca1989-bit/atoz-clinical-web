namespace AtoZClinical.Infrastructure.Services;

/// <summary>Clinic wall-clock time (Iraq / Arabia Standard Time) for appointments and reminders.</summary>
public static class ClinicClock
{
    private static readonly TimeZoneInfo ClinicZone = ResolveTimeZone();

    private static TimeZoneInfo ResolveTimeZone()
    {
        foreach (var id in new[] { "Asia/Baghdad", "Arab Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ClinicZone);

    public static DateTime Today => Now.Date;

    public static DateTime? CombineAppointment(DateTime? date, TimeSpan? time)
    {
        if (!date.HasValue) return null;

        var calendarDate = date.Value.Kind switch
        {
            DateTimeKind.Utc => TimeZoneInfo.ConvertTimeFromUtc(date.Value, ClinicZone).Date,
            _ => date.Value.Date
        };

        return calendarDate + (time ?? TimeSpan.Zero);
    }
}
