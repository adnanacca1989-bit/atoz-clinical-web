namespace AtoZClinical.Web.Api;

public sealed record CreateAppointmentApiRequest(
    Guid PatientId,
    DateTime AppointmentDate,
    TimeSpan StartTime,
    string? DoctorName,
    string? Department,
    string? Reason);

public sealed record CreatePatientApiRequest(
    string FirstName,
    string LastName,
    string? Phone,
    string? Email,
    DateTime? DateOfBirth,
    string? Gender);
