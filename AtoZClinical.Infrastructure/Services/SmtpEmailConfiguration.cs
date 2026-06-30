using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>SMTP detection from Render/process environment variables (SMTP_* only).</summary>
public static class SmtpEmailConfiguration
{
    public const string NotConfiguredUserMessage = "Email is not configured on the server.";
    public const string ConfirmationEmailSentMessage = "Confirmation email sent. Check your inbox and spam folder.";
    public const string NotConfiguredServiceMessage = ClinicalEmailSendResult.NotConfiguredMessage;
    public const string StartupSuccessMessage = "SMTP configured successfully";
    public const string OtpSendingLogMessage = "Sending OTP via email";
    public const string NotConfiguredLogMessagePrefix = "Email is NOT CONFIGURED — missing:";

    /// <summary>Operator-facing message when SMTP env vars are missing (never includes secret values).</summary>
    public static string FormatMissingConfigurationError(IConfiguration? config = null)
    {
        return FormatMissingConfigurationError(GetMissingVariables(config));
    }

    public static string FormatMissingConfigurationError(IReadOnlyList<string> missing)
    {
        if (missing.Count == 0)
            return NotConfiguredUserMessage;

        return $"SMTP is not configured. Set these environment variables on the Render Web Service: {string.Join(", ", missing)}.";
    }

    public static string FormatMissingVariablesText(IReadOnlyList<string> missing)
    {
        if (missing.Count == 0)
            return NotConfiguredUserMessage;

        var bullets = string.Join(Environment.NewLine, missing.Select(name => $"* {name}"));
        return $"Email is not configured on the server.{Environment.NewLine}{Environment.NewLine}Missing:{Environment.NewLine}{bullets}";
    }

    public static string FormatMissingVariablesHtml(IReadOnlyList<string> missing)
    {
        if (missing.Count == 0)
            return NotConfiguredUserMessage;

        var items = string.Join(string.Empty, missing.Select(name => $"<li><code>{name}</code></li>"));
        return $"<p>Email is not configured on the server.</p><p><strong>Missing:</strong></p><ul class=\"mb-0\">{items}</ul>";
    }

    public static readonly string[] RequiredVariableNames =
    [
        "SMTP_HOST",
        "SMTP_PORT",
        "SMTP_USER",
        "SMTP_PASS",
        "SMTP_FROM"
    ];

    public static bool IsEmailConfirmationEnabled(IConfiguration? config = null) =>
        IsEmailConfigured(config);

    public static bool IsEmailConfigured(IConfiguration? config = null) =>
        GetMissingVariables(config).Count == 0;

    public static IReadOnlyDictionary<string, bool> GetVariablePresence(IConfiguration? config = null)
    {
        return new Dictionary<string, bool>
        {
            ["SMTP_HOST"] = HasValue(ReadHost(config)),
            ["SMTP_PORT"] = HasValidPort(config),
            ["SMTP_USER"] = HasValue(ReadUser(config)),
            ["SMTP_PASS"] = HasValue(ReadPassword(config)),
            ["SMTP_FROM"] = HasValue(ReadExplicitFromEmail(config))
        };
    }

    public static IReadOnlyList<string> GetMissingVariables(IConfiguration? config = null)
    {
        _ = config;
        var missing = new List<string>();
        if (!HasValue(ReadHost(config))) missing.Add("SMTP_HOST");
        if (!HasValidPort(config)) missing.Add("SMTP_PORT");
        if (!HasValue(ReadUser(config))) missing.Add("SMTP_USER");
        if (!HasValue(ReadPassword(config))) missing.Add("SMTP_PASS");
        if (!HasValue(ReadExplicitFromEmail(config))) missing.Add("SMTP_FROM");
        return missing;
    }

    /// <summary>Structured payload for GET /health/email (never includes secret values).</summary>
    public static Dictionary<string, object?> BuildEmailHealthPayload(IConfiguration? config = null)
    {
        var configured = IsEmailConfigured(config);
        var missing = GetMissingVariables(config);
        var presence = GetVariablePresence(config);
        return new Dictionary<string, object?>
        {
            ["emailConfigured"] = configured,
            ["status"] = configured ? "ready" : "not_configured",
            ["missingVariables"] = missing,
            ["smtpVariables"] = presence,
            ["smtpEnvUnset"] = GetUnsetProcessEnvironmentVariables(),
            ["emailConfigurationError"] = configured ? null : FormatMissingConfigurationError(missing),
            ["smtpHost"] = configured ? ReadHost(config) : null,
            ["smtpPort"] = configured ? ReadPort(config) : null
        };
    }

    public static string FormatNotConfiguredLogMessage(IReadOnlyList<string> missing) =>
        $"{NotConfiguredLogMessagePrefix} {string.Join(", ", missing)}";

    /// <summary>
    /// Variables not set in the process environment (Render dashboard).
    /// Equivalent to checking process.env.SMTP_* in Node.js — values are never logged.
    /// </summary>
    public static IReadOnlyList<string> GetUnsetProcessEnvironmentVariables()
    {
        var missing = new List<string>();
        foreach (var name in RequiredVariableNames)
        {
            if (name == "SMTP_PORT")
            {
                var raw = Environment.GetEnvironmentVariable("SMTP_PORT");
                if (!HasValue(raw))
                {
                    missing.Add("SMTP_PORT");
                }
                else if (!int.TryParse(raw.Trim(), out var port) || port is <= 0 or > 65535)
                {
                    missing.Add("SMTP_PORT");
                }

                continue;
            }

            if (!HasValue(Environment.GetEnvironmentVariable(name)))
                missing.Add(name);
        }

        return missing;
    }

    /// <summary>Logs an error for each required SMTP env var missing on the server (never logs secret values).</summary>
    public static void LogMissingVariablesAsErrors(ILogger logger, IConfiguration? config = null)
    {
        var unsetOnProcess = GetUnsetProcessEnvironmentVariables();
        foreach (var name in unsetOnProcess)
        {
            logger.LogError(
                "SMTP environment variable {Variable} is not set. Add it to Render (or process.env / Environment Variables). OTP will use server log delivery until SMTP is configured.",
                name);
        }

        if (unsetOnProcess.Count > 0)
        {
            logger.LogError(
                "SMTP is not fully configured. Missing on server: {Missing}. Required: {Required}",
                string.Join(", ", unsetOnProcess),
                string.Join(", ", RequiredVariableNames));
            return;
        }

        if (!IsEmailConfigured(config))
        {
            var missing = GetMissingVariables(config);
            logger.LogError(
                "SMTP environment variables are present but email is still not ready. Check values for: {Missing}",
                string.Join(", ", missing));
        }
    }

    /// <summary>Logs raw process environment variable presence (values are never logged).</summary>
    public static void LogEnvironmentPresence(ILogger logger)
    {
        LogRawEnv(logger, "SMTP_HOST");
        LogRawEnv(logger, "SMTP_PORT");
        LogRawEnv(logger, "SMTP_USER");
        LogRawEnv(logger, "SMTP_PASS");
        LogRawEnv(logger, "SMTP_FROM");
        LogRawEnv(logger, "FROM_EMAIL");
    }

    public static void LogDiagnostics(ILogger logger, IConfiguration? config = null)
    {
        LogStartupEmailStatus(logger, config);

        if (IsEmailConfigured(config))
        {
            logger.LogInformation(
                "SMTP email ready: {Host}:{Port}",
                ReadHost(config),
                ReadPort(config));
        }
    }

    /// <summary>Primary startup log: whether outbound email (SMTP) is configured.</summary>
    public static void LogStartupEmailStatus(ILogger logger, IConfiguration? config = null)
    {
        var presence = GetVariablePresence(config);
        var configured = IsEmailConfigured(config);

        logger.LogInformation(
            "SMTP startup check — emailConfigured={Configured} | HOST={Host} PORT={Port} USER={User} PASS={Pass} FROM={From}",
            configured,
            presence["SMTP_HOST"],
            presence["SMTP_PORT"],
            presence["SMTP_USER"],
            presence["SMTP_PASS"],
            presence["SMTP_FROM"]);

        if (configured)
        {
            logger.LogInformation(StartupSuccessMessage);
            logger.LogInformation(
                "Email is CONFIGURED — SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS, and SMTP_FROM are set (Gmail: host=smtp.gmail.com, port=587, STARTTLS).");
            return;
        }

        var unsetOnProcess = GetUnsetProcessEnvironmentVariables();
        var missing = unsetOnProcess.Count > 0 ? unsetOnProcess : GetMissingVariables(config);
        logger.LogError(FormatNotConfiguredLogMessage(missing));
        logger.LogError(
            "Add these on Render (Web Service → Environment): {Required}",
            string.Join(", ", RequiredVariableNames));
    }

    /// <summary>One-line developer summary for Render logs at startup (never logs secret values).</summary>
    public static void LogStartupDeveloperSummary(ILogger logger, IConfiguration? config = null)
    {
        LogStartupEmailStatus(logger, config);
    }

    private static void LogRawEnv(ILogger logger, string name)
    {
        var exists = HasValue(Environment.GetEnvironmentVariable(name));
        logger.LogInformation("{Name}: {Exists}", name, exists);
    }

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool HasValidPort(IConfiguration? config)
    {
        var raw = ReadPortRaw(config);
        return HasValue(raw)
            && int.TryParse(raw!.Trim(), out var port)
            && port is > 0 and <= 65535;
    }

    internal static string? ReadHost(IConfiguration? config) => ReadRequiredEnv("SMTP_HOST");

    internal static string? ReadPortRaw(IConfiguration? config) => ReadRequiredEnv("SMTP_PORT");

    internal static int ReadPort(IConfiguration? config)
    {
        var raw = ReadPortRaw(config);
        return HasValue(raw) && int.TryParse(raw!.Trim(), out var port) ? port : 587;
    }

    internal static string? ReadUser(IConfiguration? config) => ReadRequiredEnv("SMTP_USER");

    internal static string? ReadPassword(IConfiguration? config) => ReadRequiredEnv("SMTP_PASS");

    internal static string? ReadExplicitFromEmail(IConfiguration? config) => ReadRequiredEnv("SMTP_FROM");

    internal static string? ReadFromEmail(IConfiguration? config)
    {
        var from = ReadExplicitFromEmail(config);
        if (HasValue(from))
            return from;

        var user = ReadUser(config);
        var host = ReadHost(config);
        if (HasValue(user) && SmtpEmailDiagnostics.IsGmailHost(host))
            return user;

        return from;
    }

    internal static string? ReadFromName(IConfiguration? config) =>
        ReadOptionalEnv("FROM_NAME") ?? "A to Z Clinical";

    internal static bool ReadUseSsl(IConfiguration? config)
    {
        var raw = ReadOptionalEnv("SMTP_USE_SSL");
        if (HasValue(raw) && bool.TryParse(raw!.Trim(), out var ssl))
            return ssl;
        return ReadPort(config) is 465 or 587;
    }

    private static string? ReadRequiredEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return HasValue(value) ? value!.Trim() : null;
    }

    private static string? ReadOptionalEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return HasValue(value) ? value!.Trim() : null;
    }
}
