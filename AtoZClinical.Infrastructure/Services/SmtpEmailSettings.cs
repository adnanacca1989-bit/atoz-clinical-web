using MailKit.Security;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>Resolves SMTP settings from appsettings and Render-friendly environment variables.</summary>
public sealed class SmtpEmailSettings
{
    public bool Enabled { get; init; }
    public string? Host { get; init; }
    public int Port { get; init; } = 587;
    public string? User { get; init; }
    public string? Password { get; init; }
    public string? FromAddress { get; init; }
    public string FromName { get; init; } = "A to Z Clinical";
    public bool UseSsl { get; init; } = true;

    public bool IsReady =>
        Enabled &&
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(FromAddress) &&
        HasRequiredCredentials();

    public SecureSocketOptions SecureSocketOptions =>
        !UseSsl ? SecureSocketOptions.None :
        Port == 465 ? SecureSocketOptions.SslOnConnect :
        Port == 587 ? SecureSocketOptions.StartTls :
        SecureSocketOptions.Auto;

    public string DescribeReadiness()
    {
        if (!Enabled) return "disabled (set SMTP_HOST and FROM_EMAIL)";
        if (string.IsNullOrWhiteSpace(Host)) return "missing SMTP_HOST";
        if (string.IsNullOrWhiteSpace(FromAddress)) return "missing FROM_EMAIL";
        if (string.IsNullOrWhiteSpace(User)) return "missing SMTP_USER";
        if (string.IsNullOrWhiteSpace(Password)) return "missing SMTP_PASS";
        return "ready";
    }

    private bool HasRequiredCredentials()
    {
        if (Host is "localhost" or "127.0.0.1")
            return true;

        return !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Password);
    }

    public static SmtpEmailSettings From(IConfiguration config)
    {
        static string? First(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }

        var host = First(
            config["Email:SmtpHost"],
            config["SMTP_HOST"]);

        var fromAddress = First(
            config["Email:FromAddress"],
            config["FROM_EMAIL"]);

        var portRaw = First(
            config["Email:SmtpPort"],
            config["SMTP_PORT"]);
        var port = int.TryParse(portRaw, out var parsedPort) ? parsedPort : 587;

        var useSslRaw = First(
            config["Email:UseSsl"],
            config["SMTP_USE_SSL"]);
        var useSsl = string.IsNullOrWhiteSpace(useSslRaw)
            ? port is 465 or 587
            : bool.TryParse(useSslRaw, out var ssl) && ssl;

        var user = First(
            config["Email:SmtpUser"],
            config["SMTP_USER"]);
        var password = First(
            config["Email:SmtpPassword"],
            config["SMTP_PASS"],
            config["SMTP_PASSWORD"]);

        var hasMinimum = !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(fromAddress);
        var enabled = config.GetValue("Email:Enabled", hasMinimum);

        return new SmtpEmailSettings
        {
            Enabled = enabled && hasMinimum,
            Host = host,
            Port = port,
            User = user,
            Password = password,
            FromAddress = fromAddress,
            FromName = First(
                config["Email:FromName"],
                config["FROM_NAME"]) ?? "A to Z Clinical",
            UseSsl = useSsl
        };
    }
}
