using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>SMTP detection: Render environment variables first, then IConfiguration fallback.</summary>
public static class SmtpEmailConfiguration
{
    public const string NotConfiguredUserMessage = "Email is not configured on the server.";
    public const string NotConfiguredServiceMessage = ClinicalEmailSendResult.NotConfiguredMessage;

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
        "FROM_EMAIL"
    ];

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
            ["FROM_EMAIL"] = HasValue(ReadFromEmail(config))
        };
    }

    public static IReadOnlyList<string> GetMissingVariables(IConfiguration? config = null)
    {
        var missing = new List<string>();
        if (!HasValue(ReadHost(config))) missing.Add("SMTP_HOST");
        if (!HasValidPort(config)) missing.Add("SMTP_PORT");
        if (!HasValue(ReadUser(config))) missing.Add("SMTP_USER");
        if (!HasValue(ReadPassword(config))) missing.Add("SMTP_PASS");
        if (!HasValue(ReadFromEmail(config))) missing.Add("FROM_EMAIL");
        return missing;
    }

    /// <summary>Logs raw process environment variable presence (values are never logged).</summary>
    public static void LogEnvironmentPresence(ILogger logger)
    {
        LogRawEnv(logger, "SMTP_HOST");
        LogRawEnv(logger, "SMTP_PORT");
        LogRawEnv(logger, "SMTP_USER");
        LogRawEnv(logger, "SMTP_PASS");
        LogRawEnv(logger, "FROM_EMAIL");
    }

    public static void LogDiagnostics(ILogger logger, IConfiguration? config = null)
    {
        var presence = GetVariablePresence(config);
        logger.LogInformation("SMTP_HOST: {Exists}", presence["SMTP_HOST"]);
        logger.LogInformation("SMTP_PORT: {Exists}", presence["SMTP_PORT"]);
        logger.LogInformation("SMTP_USER: {Exists}", presence["SMTP_USER"]);
        logger.LogInformation("SMTP_PASS: {Exists}", presence["SMTP_PASS"]);
        logger.LogInformation("FROM_EMAIL: {Exists}", presence["FROM_EMAIL"]);

        if (IsEmailConfigured(config))
        {
            logger.LogInformation(
                "SMTP email ready: {Host}:{Port}",
                ReadHost(config),
                ReadPort(config));
        }
        else
        {
            var missing = GetMissingVariables(config);
            logger.LogWarning(
                "SMTP not configured: missing variables: {Missing}",
                string.Join(", ", missing));
        }
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

    internal static string? ReadHost(IConfiguration? config) =>
        ReadEnvFirst("SMTP_HOST", "Email__SmtpHost")
        ?? ReadConfig(config, "Email:SmtpHost");

    internal static string? ReadPortRaw(IConfiguration? config) =>
        ReadEnvFirst("SMTP_PORT", "Email__SmtpPort")
        ?? ReadConfig(config, "Email:SmtpPort");

    internal static int ReadPort(IConfiguration? config)
    {
        var raw = ReadPortRaw(config);
        return HasValue(raw) && int.TryParse(raw!.Trim(), out var port) ? port : 587;
    }

    internal static string? ReadUser(IConfiguration? config) =>
        ReadEnvFirst("SMTP_USER", "Email__SmtpUser")
        ?? ReadConfig(config, "Email:SmtpUser");

    internal static string? ReadPassword(IConfiguration? config) =>
        ReadEnvFirst("SMTP_PASS", "SMTP_PASSWORD", "Email__SmtpPassword")
        ?? ReadConfig(config, "Email:SmtpPassword");

    internal static string? ReadExplicitFromEmail(IConfiguration? config) =>
        ReadEnvFirst("FROM_EMAIL", "Email__FromAddress")
        ?? ReadConfig(config, "Email:FromAddress");

    internal static string? ReadFromEmail(IConfiguration? config)
    {
        var from = ReadEnvFirst("FROM_EMAIL", "Email__FromAddress")
            ?? ReadConfig(config, "Email:FromAddress");
        if (HasValue(from))
            return from;

        var user = ReadUser(config);
        var host = ReadHost(config);
        if (HasValue(user) && SmtpEmailDiagnostics.IsGmailHost(host))
            return user;

        return from;
    }

    internal static string? ReadFromName(IConfiguration? config) =>
        ReadEnvFirst("FROM_NAME", "Email__FromName")
        ?? ReadConfig(config, "Email:FromName")
        ?? "A to Z Clinical";

    internal static bool ReadUseSsl(IConfiguration? config)
    {
        var raw = ReadEnvFirst("SMTP_USE_SSL", "Email__UseSsl")
            ?? ReadConfig(config, "Email:UseSsl");
        if (HasValue(raw) && bool.TryParse(raw!.Trim(), out var ssl))
            return ssl;
        return ReadPort(config) is 465 or 587;
    }

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
