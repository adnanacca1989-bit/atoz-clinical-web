using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>Maps Render/plain SMTP environment variables into Email:* configuration keys.</summary>
public static class SmtpConfigurationBootstrap
{
    public static void Apply(ConfigurationManager configuration)
    {
        static void SetIfPresent(ConfigurationManager config, string envName, string configKey)
        {
            var value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value))
                config[configKey] = value.Trim();
        }

        SetIfPresent(configuration, "SMTP_HOST", "Email:SmtpHost");
        SetIfPresent(configuration, "SMTP_PORT", "Email:SmtpPort");
        SetIfPresent(configuration, "SMTP_USER", "Email:SmtpUser");
        SetIfPresent(configuration, "SMTP_PASS", "Email:SmtpPassword");
        SetIfPresent(configuration, "SMTP_PASSWORD", "Email:SmtpPassword");
        SetIfPresent(configuration, "FROM_EMAIL", "Email:FromAddress");
        SetIfPresent(configuration, "FROM_NAME", "Email:FromName");
        SetIfPresent(configuration, "SMTP_USE_SSL", "Email:UseSsl");

        var host = configuration["Email:SmtpHost"];
        var from = configuration["Email:FromAddress"];
        if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(from))
            configuration["Email:Enabled"] = "true";
    }
}
