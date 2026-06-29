using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeKit;

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
            var reason = settings.DescribeReadiness();
            if (_env.IsDevelopment())
            {
                _logger.LogWarning(
                    "Email not configured ({Reason}). Would send to {To}: {Subject}",
                    reason, toEmail, subject);
                return;
            }

            throw new InvalidOperationException($"Email is not configured ({reason}).");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail.Trim()));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(settings.Host!, settings.Port, settings.SecureSocketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(settings.User))
                await client.AuthenticateAsync(settings.User, settings.Password, cancellationToken);

            var response = await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation(
                "Email sent to {To}: {Subject} via {Host}:{Port} (response: {Response})",
                toEmail, subject, settings.Host, settings.Port, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send email to {To}: {Subject} via {Host}:{Port} ({SocketOption})",
                toEmail, subject, settings.Host, settings.Port, settings.SecureSocketOptions);
            throw;
        }
    }
}
