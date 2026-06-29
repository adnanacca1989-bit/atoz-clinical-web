using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace AtoZClinical.Infrastructure.Services;

public interface IClinicalEmailSender
{
    bool IsConfigured { get; }
    Task<ClinicalEmailSendResult> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default);
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

    public bool IsConfigured => SmtpEmailConfiguration.IsEmailConfigured(_config);

    public async Task<ClinicalEmailSendResult> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            throw new ArgumentException("Recipient email is required.", nameof(toEmail));

        var settings = SmtpEmailSettings.From(_config);
        if (!settings.IsReady)
        {
            var missing = SmtpEmailConfiguration.GetMissingVariables(_config);
            _logger.LogWarning(
                "Email skipped (not configured) for {To}. Missing: {Missing}",
                toEmail.Trim(),
                string.Join(", ", missing));

            if (_env.IsDevelopment())
            {
                _logger.LogWarning(
                    "Development mode: would send to {To}: {Subject}",
                    toEmail.Trim(), subject);
            }

            return ClinicalEmailSendResult.SkippedNotConfigured();
        }

        if (!string.IsNullOrWhiteSpace(settings.ConfigurationWarning))
            _logger.LogWarning(settings.ConfigurationWarning);

        _logger.LogInformation(
            "Sending email to {To} subject={Subject} via {Host}:{Port} ({SocketOption}) from {From}",
            toEmail, subject, settings.Host, settings.Port, settings.SecureSocketOptions, settings.FromAddress);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail.Trim()));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        try
        {
            _logger.LogInformation("Connecting to SMTP {Host}:{Port}...", settings.Host, settings.Port);
            await client.ConnectAsync(settings.Host!, settings.Port, settings.SecureSocketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(settings.User))
            {
                _logger.LogInformation("Authenticating SMTP user {User}...", settings.User);
                await client.AuthenticateAsync(settings.User, settings.Password, cancellationToken);
            }

            var response = await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation(
                "Email sent successfully to {To} via {Host}:{Port} (response: {Response})",
                toEmail, settings.Host, settings.Port, response);

            return ClinicalEmailSendResult.Sent();
        }
        catch (Exception ex)
        {
            var reason = SmtpEmailDiagnostics.ClassifyFailure(ex);
            _logger.LogError(ex,
                "Email send failed to {To} via {Host}:{Port}: {FailureReason}",
                toEmail, settings.Host, settings.Port, reason);
            return ClinicalEmailSendResult.Failed(reason);
        }
    }
}
