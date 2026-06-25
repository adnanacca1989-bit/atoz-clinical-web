using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

namespace AtoZClinical.Infrastructure.Services;

public interface IClinicalEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
}

public sealed class SmtpClinicalEmailSender : IClinicalEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpClinicalEmailSender> _logger;
    private readonly IHostEnvironment _env;

    public SmtpClinicalEmailSender(IConfiguration config, ILogger<SmtpClinicalEmailSender> logger, IHostEnvironment env)
    {
        _config = config;
        _logger = logger;
        _env = env;
    }

    public bool IsConfigured =>
        _config.GetValue("Email:Enabled", false) &&
        !string.IsNullOrWhiteSpace(_config["Email:SmtpHost"]) &&
        !string.IsNullOrWhiteSpace(_config["Email:FromAddress"]);

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            throw new ArgumentException("Recipient email is required.", nameof(toEmail));

        if (!IsConfigured)
        {
            if (_env.IsDevelopment())
            {
                _logger.LogWarning(
                    "Email not configured. Would send to {To}: {Subject}\n{Body}",
                    toEmail, subject, htmlBody);
                return;
            }

            throw new InvalidOperationException(
                "Email is not configured. Set Email:Enabled, Email:SmtpHost, and Email:FromAddress.");
        }

        var host = _config["Email:SmtpHost"]!.Trim();
        var port = _config.GetValue("Email:SmtpPort", 587);
        var user = _config["Email:SmtpUser"];
        var password = _config["Email:SmtpPassword"];
        var fromAddress = _config["Email:FromAddress"]!.Trim();
        var fromName = _config["Email:FromName"] ?? "A to Z Clinical";
        var useSsl = _config.GetValue("Email:UseSsl", port == 465);

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress, fromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(toEmail.Trim());

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = useSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(user))
            client.Credentials = new NetworkCredential(user, password);

        await client.SendMailAsync(message, cancellationToken);
        _logger.LogInformation("Email sent to {To}: {Subject}", toEmail, subject);
    }
}
