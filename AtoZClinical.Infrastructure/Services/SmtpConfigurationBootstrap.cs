using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>Maps Render/plain SMTP environment variables into Email:* configuration keys.</summary>
public static class SmtpConfigurationBootstrap
{
    public static void Apply(ConfigurationManager configuration)
    {
        static void SetIfPresent(ConfigurationManager config, string configKey, params string[] envNames)
        {
            foreach (var envName in envNames)
            {
                var value = Environment.GetEnvironmentVariable(envName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    config[configKey] = value.Trim();
                    return;
                }
            }
        }

        SetIfPresent(configuration, "Email:SmtpHost", "SMTP_HOST");
        SetIfPresent(configuration, "Email:SmtpPort", "SMTP_PORT");
        SetIfPresent(configuration, "Email:SmtpUser", "SMTP_USER");
        SetIfPresent(configuration, "Email:SmtpPassword", "SMTP_PASS");
        SetIfPresent(configuration, "Email:FromAddress", "SMTP_FROM");
        SetIfPresent(configuration, "Email:FromName", "FROM_NAME");
        SetIfPresent(configuration, "Email:UseSsl", "SMTP_USE_SSL");

        var host = configuration["Email:SmtpHost"];
        var user = configuration["Email:SmtpUser"];
        var from = configuration["Email:FromAddress"];

        if (string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(user))
            configuration["Email:FromAddress"] = user;

        if (SmtpEmailDiagnostics.IsGmailHost(host) && !string.IsNullOrWhiteSpace(user))
            configuration["Email:FromAddress"] = user;

        if (!string.IsNullOrWhiteSpace(configuration["Email:SmtpHost"])
            && !string.IsNullOrWhiteSpace(configuration["Email:FromAddress"]))
            configuration["Email:Enabled"] = "true";
    }
}
