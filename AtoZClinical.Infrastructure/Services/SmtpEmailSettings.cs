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
        !string.IsNullOrWhiteSpace(FromAddress);

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
            config["SMTP_HOST"],
            Environment.GetEnvironmentVariable("SMTP_HOST"));

        var fromAddress = First(
            config["Email:FromAddress"],
            config["FROM_EMAIL"],
            Environment.GetEnvironmentVariable("FROM_EMAIL"));

        var portRaw = First(
            config["Email:SmtpPort"],
            config["SMTP_PORT"],
            Environment.GetEnvironmentVariable("SMTP_PORT"));
        var port = int.TryParse(portRaw, out var parsedPort) ? parsedPort : 587;

        var useSslRaw = First(
            config["Email:UseSsl"],
            config["SMTP_USE_SSL"],
            Environment.GetEnvironmentVariable("SMTP_USE_SSL"));
        var useSsl = string.IsNullOrWhiteSpace(useSslRaw)
            ? port is 465 or 587
            : bool.TryParse(useSslRaw, out var ssl) && ssl;

        var hasSmtpEnv = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMTP_HOST"))
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FROM_EMAIL"));

        var enabled = hasSmtpEnv || config.GetValue(
            "Email:Enabled",
            !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(fromAddress));

        return new SmtpEmailSettings
        {
            Enabled = enabled,
            Host = host,
            Port = port,
            User = First(
                config["Email:SmtpUser"],
                config["SMTP_USER"],
                Environment.GetEnvironmentVariable("SMTP_USER")),
            Password = First(
                config["Email:SmtpPassword"],
                config["SMTP_PASS"],
                config["SMTP_PASSWORD"],
                Environment.GetEnvironmentVariable("SMTP_PASS"),
                Environment.GetEnvironmentVariable("SMTP_PASSWORD")),
            FromAddress = fromAddress,
            FromName = First(
                config["Email:FromName"],
                config["FROM_NAME"],
                Environment.GetEnvironmentVariable("FROM_NAME")) ?? "A to Z Clinical",
            UseSsl = useSsl
        };
    }
}
