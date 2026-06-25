namespace AtoZClinical.Core.Webhooks;

public static class WebhookEvents
{
    public const string PatientCreated = "patient.created";
    public const string PatientUpdated = "patient.updated";
    public const string AppointmentCreated = "appointment.created";

    public static readonly string[] All =
    [
        PatientCreated,
        PatientUpdated,
        AppointmentCreated
    ];
}
