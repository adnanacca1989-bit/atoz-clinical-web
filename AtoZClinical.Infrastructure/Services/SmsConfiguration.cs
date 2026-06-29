using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

public static class SmsConfiguration
{
    public const string NotConfiguredUserMessage = "SMS is not configured on the server.";

    public static readonly string[] RequiredVariableNames =
    [
        "TWILIO_ACCOUNT_SID",
        "TWILIO_AUTH_TOKEN",
        "TWILIO_FROM_NUMBER"
    ];

    public static bool IsSmsConfigured(IConfiguration? config = null) =>
        GetMissingVariables(config).Count == 0;

    public static IReadOnlyList<string> GetMissingVariables(IConfiguration? config = null)
    {
        var missing = new List<string>();
        if (!HasValue(ReadAccountSid(config))) missing.Add("TWILIO_ACCOUNT_SID");
        if (!HasValue(ReadAuthToken(config))) missing.Add("TWILIO_AUTH_TOKEN");
        if (!HasValue(ReadFromNumber(config))) missing.Add("TWILIO_FROM_NUMBER");
        return missing;
    }

    public static void LogDiagnostics(ILogger logger, IConfiguration? config = null)
    {
        var presence = GetVariablePresence(config);
        logger.LogInformation("TWILIO_ACCOUNT_SID: {Exists}", presence["TWILIO_ACCOUNT_SID"]);
        logger.LogInformation("TWILIO_AUTH_TOKEN: {Exists}", presence["TWILIO_AUTH_TOKEN"]);
        logger.LogInformation("TWILIO_FROM_NUMBER: {Exists}", presence["TWILIO_FROM_NUMBER"]);

        if (IsSmsConfigured(config))
            logger.LogInformation("Twilio SMS ready from {From}", ReadFromNumber(config));
        else
            logger.LogWarning("SMS not configured: missing {Missing}", string.Join(", ", GetMissingVariables(config)));
    }

    public static IReadOnlyDictionary<string, bool> GetVariablePresence(IConfiguration? config = null) =>
        new Dictionary<string, bool>
        {
            ["TWILIO_ACCOUNT_SID"] = HasValue(ReadAccountSid(config)),
            ["TWILIO_AUTH_TOKEN"] = HasValue(ReadAuthToken(config)),
            ["TWILIO_FROM_NUMBER"] = HasValue(ReadFromNumber(config))
        };

    internal static string? ReadAccountSid(IConfiguration? config) =>
        ReadEnvFirst("TWILIO_ACCOUNT_SID", "Sms__TwilioAccountSid")
        ?? ReadConfig(config, "Sms:TwilioAccountSid");

    internal static string? ReadAuthToken(IConfiguration? config) =>
        ReadEnvFirst("TWILIO_AUTH_TOKEN", "Sms__TwilioAuthToken")
        ?? ReadConfig(config, "Sms:TwilioAuthToken");

    internal static string? ReadFromNumber(IConfiguration? config) =>
        ReadEnvFirst("TWILIO_FROM_NUMBER", "Sms__TwilioFromNumber")
        ?? ReadConfig(config, "Sms:TwilioFromNumber");

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string? ReadEnvFirst(params string[] envNames)
    {
        foreach (var name in envNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (HasValue(value))
                return value!.Trim();
        }

        return null;
    }

    private static string? ReadConfig(IConfiguration? config, params string[] keys)
    {
        if (config is null)
            return null;

        foreach (var key in keys)
        {
            var value = config[key];
            if (HasValue(value))
                return value!.Trim();
        }

        return null;
    }
}
