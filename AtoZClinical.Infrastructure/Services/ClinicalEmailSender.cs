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

    public bool IsConfigured => SmtpEmailSettings.From(_config).IsReady;

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            throw new ArgumentException("Recipient email is required.", nameof(toEmail));

        var settings = SmtpEmailSettings.From(_config);
        if (!settings.IsReady)
        {
            if (_env.IsDevelopment())
            {
                _logger.LogWarning(
                    "Email not configured. Would send to {To}: {Subject}\n{Body}",
                    toEmail, subject, htmlBody);
                return;
            }

            throw new InvalidOperationException(
                "Email is not configured. Set SMTP_HOST and FROM_EMAIL (or Email:SmtpHost and Email:FromAddress).");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress!, settings.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(toEmail.Trim());

        using var client = new SmtpClient(settings.Host!, settings.Port)
        {
            EnableSsl = settings.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(settings.User))
            client.Credentials = new NetworkCredential(settings.User, settings.Password);

        try
        {
            await client.SendMailAsync(message, cancellationToken);
            _logger.LogInformation("Email sent to {To}: {Subject} via {Host}:{Port}", toEmail, subject, settings.Host, settings.Port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send email to {To}: {Subject} via {Host}:{Port}",
                toEmail, subject, settings.Host, settings.Port);
            throw;
        }
    }
}
