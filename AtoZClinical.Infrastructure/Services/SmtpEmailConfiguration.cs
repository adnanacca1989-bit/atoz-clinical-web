using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>SMTP detection from Render environment variables (primary) and appsettings (fallback).</summary>
public static class SmtpEmailConfiguration
{
    public const string NotConfiguredUserMessage = "Email is not configured on the server.";

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

    public static IReadOnlyList<string> GetMissingVariables(IConfiguration? config = null)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ReadHost(config))) missing.Add("SMTP_HOST");
        if (string.IsNullOrWhiteSpace(ReadPortRaw(config))) missing.Add("SMTP_PORT");
        if (string.IsNullOrWhiteSpace(ReadUser(config))) missing.Add("SMTP_USER");
        if (string.IsNullOrWhiteSpace(ReadPassword(config))) missing.Add("SMTP_PASS");
        if (string.IsNullOrWhiteSpace(ReadFromEmail(config))) missing.Add("FROM_EMAIL");
        return missing;
    }

    public static void LogDiagnostics(ILogger logger, IConfiguration config)
    {
        LogVariable(logger, "SMTP_HOST", ReadHost(config));
        LogVariable(logger, "SMTP_PORT", ReadPortRaw(config));
        LogVariable(logger, "SMTP_USER", ReadUser(config));
        LogVariable(logger, "SMTP_PASS", ReadPassword(config));
        LogVariable(logger, "FROM_EMAIL", ReadFromEmail(config));

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

    private static void LogVariable(ILogger logger, string name, string? resolvedValue)
    {
        var fromEnv = Environment.GetEnvironmentVariable(name);
        var status = !string.IsNullOrWhiteSpace(fromEnv) ? "loaded (env)"
            : !string.IsNullOrWhiteSpace(resolvedValue) ? "loaded (config)"
            : "missing";
        logger.LogInformation("SMTP variable {Name}: {Status}", name, status);
    }

    internal static string? ReadHost(IConfiguration? config) =>
        Read("SMTP_HOST", config, "Email:SmtpHost", "SMTP_HOST");

    internal static string? ReadPortRaw(IConfiguration? config) =>
        Read("SMTP_PORT", config, "Email:SmtpPort", "SMTP_PORT");

    internal static int ReadPort(IConfiguration? config) =>
        int.TryParse(ReadPortRaw(config), out var port) ? port : 587;

    internal static string? ReadUser(IConfiguration? config) =>
        Read("SMTP_USER", config, "Email:SmtpUser", "SMTP_USER");

    internal static string? ReadPassword(IConfiguration? config) =>
        Read("SMTP_PASS", config, "Email:SmtpPassword", "SMTP_PASS", "SMTP_PASSWORD");

    internal static string? ReadFromEmail(IConfiguration? config)
    {
        var from = Read("FROM_EMAIL", config, "Email:FromAddress", "FROM_EMAIL");
        if (!string.IsNullOrWhiteSpace(from))
            return from;

        var user = ReadUser(config);
        var host = ReadHost(config);
        if (!string.IsNullOrWhiteSpace(user) && SmtpEmailDiagnostics.IsGmailHost(host))
            return user;

        return from;
    }

    internal static string? ReadFromName(IConfiguration? config) =>
        Read("FROM_NAME", config, "Email:FromName", "FROM_NAME") ?? "A to Z Clinical";

    internal static bool ReadUseSsl(IConfiguration? config)
    {
        var raw = Read("SMTP_USE_SSL", config, "Email:UseSsl", "SMTP_USE_SSL");
        if (!string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw, out var ssl))
            return ssl;
        return ReadPort(config) is 465 or 587;
    }

    internal static string? Read(string envName, IConfiguration? config, params string[] configKeys)
    {
        var env = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        return ReadConfigFallback(envName, config, configKeys);
    }

    private static string? ReadConfigFallback(string envName, IConfiguration? config, params string[] configKeys)
    {
        if (config is null)
            return null;

        var keys = configKeys.Length > 0 ? configKeys : [envName];
        foreach (var key in keys)
        {
            var value = config[key];
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}
