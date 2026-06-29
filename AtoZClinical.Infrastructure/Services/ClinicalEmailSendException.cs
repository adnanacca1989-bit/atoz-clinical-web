namespace AtoZClinical.Infrastructure.Services;

public sealed class ClinicalEmailSendException : Exception
{
    public ClinicalEmailSendException(string failureReason, Exception innerException)
        : base(SmtpEmailDiagnostics.UserFriendlyFailureMessage, innerException)
    {
        FailureReason = failureReason;
    }

    public string FailureReason { get; }
}
