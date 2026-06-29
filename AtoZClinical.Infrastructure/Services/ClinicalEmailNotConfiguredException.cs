namespace AtoZClinical.Infrastructure.Services;

public sealed class ClinicalEmailNotConfiguredException : Exception
{
    public ClinicalEmailNotConfiguredException(IReadOnlyList<string> missingVariables)
        : base(BuildMessage(missingVariables))
    {
        MissingVariables = missingVariables;
    }

    public IReadOnlyList<string> MissingVariables { get; }

    private static string BuildMessage(IReadOnlyList<string> missing) =>
        missing.Count == 0
            ? SmtpEmailConfiguration.NotConfiguredUserMessage
            : $"SMTP is not configured. Missing: {string.Join(", ", missing)}";
}
