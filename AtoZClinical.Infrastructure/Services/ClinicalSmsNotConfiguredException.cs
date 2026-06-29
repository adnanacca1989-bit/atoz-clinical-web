namespace AtoZClinical.Infrastructure.Services;

public sealed class ClinicalSmsNotConfiguredException : Exception
{
    public ClinicalSmsNotConfiguredException(IReadOnlyList<string> missingVariables)
        : base(BuildMessage(missingVariables))
    {
        MissingVariables = missingVariables;
    }

    public IReadOnlyList<string> MissingVariables { get; }

    private static string BuildMessage(IReadOnlyList<string> missing) =>
        missing.Count == 0
            ? SmsConfiguration.NotConfiguredUserMessage
            : $"SMS is not configured. Missing: {string.Join(", ", missing)}";
}
