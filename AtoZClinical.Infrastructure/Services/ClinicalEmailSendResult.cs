namespace AtoZClinical.Infrastructure.Services;

public sealed record ClinicalEmailSendResult(bool Success, bool Skipped, string Message)
{
    public const string NotConfiguredMessage = "Email service is not configured yet";

    public static ClinicalEmailSendResult Sent(string message = "Email sent successfully") =>
        new(true, false, message);

    public static ClinicalEmailSendResult SkippedNotConfigured() =>
        new(false, true, NotConfiguredMessage);

    public static ClinicalEmailSendResult Failed(string message) =>
        new(false, false, message);
}
