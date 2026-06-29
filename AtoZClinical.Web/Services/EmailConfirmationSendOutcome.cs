using AtoZClinical.Infrastructure.Services;

namespace AtoZClinical.Web.Services;

public sealed class EmailConfirmationSendOutcome
{
    public EmailConfirmationSendResult Result { get; init; }
    public string? ErrorMessage { get; init; }

    public static EmailConfirmationSendOutcome AlreadyConfirmed() =>
        new() { Result = EmailConfirmationSendResult.AlreadyConfirmed };

    public static EmailConfirmationSendOutcome Sent() =>
        new() { Result = EmailConfirmationSendResult.Sent };

    public static EmailConfirmationSendOutcome NotConfigured() =>
        new() { Result = EmailConfirmationSendResult.NotConfigured };

    public static EmailConfirmationSendOutcome Failed(string? errorMessage) =>
        new()
        {
            Result = EmailConfirmationSendResult.Failed,
            ErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
                ? SmtpEmailDiagnostics.UserFriendlyFailureMessage
                : errorMessage.Trim()
        };
}
