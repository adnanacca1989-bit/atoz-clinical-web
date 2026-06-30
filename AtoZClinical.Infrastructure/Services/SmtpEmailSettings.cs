using MailKit.Security;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>Resolved SMTP settings for sending mail via MailKit.</summary>
public sealed class SmtpEmailSettings
{
    public string? Host { get; init; }
    public int Port { get; init; } = 587;
    public string? User { get; init; }
    public string? Password { get; init; }
    public string? FromAddress { get; init; }
    public string FromName { get; init; } = "A to Z Clinical";
    public bool UseSsl { get; init; } = true;
    public string? ConfigurationWarning { get; init; }

    public bool IsReady =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(FromAddress) &&
        !string.IsNullOrWhiteSpace(User) &&
        !string.IsNullOrWhiteSpace(Password) &&
        Port > 0;

    public static bool IsEmailConfigured(IConfiguration? config = null) =>
        SmtpEmailConfiguration.IsEmailConfigured(config);

    public IReadOnlyList<string> ListMissingVariables() =>
        SmtpEmailConfiguration.GetMissingVariables();

    public string DescribeReadiness()
    {
        var missing = ListMissingVariables();
        return missing.Count == 0 ? "ready" : "missing " + string.Join(", ", missing);
    }

    public SecureSocketOptions SecureSocketOptions =>
        Port == 587 ? SecureSocketOptions.StartTls :
        Port == 465 ? SecureSocketOptions.SslOnConnect :
        UseSsl ? SecureSocketOptions.StartTlsWhenAvailable :
        SecureSocketOptions.None;

    public string StartupLogMessage() =>
        IsReady
            ? $"SMTP email ready: {Host}:{Port}"
            : $"SMTP not configured: missing variables: {string.Join(", ", ListMissingVariables())}";

    public static SmtpEmailSettings From(IConfiguration? config = null)
    {
        var host = SmtpEmailConfiguration.ReadHost(config);
        var user = SmtpEmailConfiguration.ReadUser(config);
        var fromAddress = SmtpEmailConfiguration.ReadFromEmail(config);

        string? warning = null;
        var configuredFrom = SmtpEmailConfiguration.ReadExplicitFromEmail(config);
        if (SmtpEmailDiagnostics.IsGmailHost(host)
            && !string.IsNullOrWhiteSpace(user)
            && !string.IsNullOrWhiteSpace(configuredFrom)
            && !string.Equals(configuredFrom, user, StringComparison.OrdinalIgnoreCase))
        {
            warning = $"FROM_EMAIL ({configuredFrom}) did not match SMTP_USER; using {user} for Gmail.";
            fromAddress = user;
        }

        return new SmtpEmailSettings
        {
            Host = host,
            Port = SmtpEmailConfiguration.ReadPort(config),
            User = user,
            Password = SmtpEmailConfiguration.ReadPassword(config),
            FromAddress = fromAddress,
            FromName = SmtpEmailConfiguration.ReadFromName(config) ?? "A to Z Clinical",
            UseSsl = SmtpEmailConfiguration.ReadUseSsl(config),
            ConfigurationWarning = warning
        };
    }
}
