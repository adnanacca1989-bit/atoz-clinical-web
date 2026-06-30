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
        SmtpEmailConfiguration.FormatMissingConfigurationError(missing);
}
