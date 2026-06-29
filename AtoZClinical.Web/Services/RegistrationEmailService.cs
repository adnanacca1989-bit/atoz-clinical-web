using System.Text;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Web.Services;

public sealed class RegistrationEmailService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IClinicalEmailSender _email;
    private readonly ClinicalAppUrls _urls;
    private readonly IConfiguration _config;
    private readonly ILogger<RegistrationEmailService> _logger;

    public RegistrationEmailService(
        UserManager<ApplicationUser> users,
        IClinicalEmailSender email,
        ClinicalAppUrls urls,
        IConfiguration config,
        ILogger<RegistrationEmailService> logger)
    {
        _users = users;
        _email = email;
        _urls = urls;
        _config = config;
        _logger = logger;
    }

    public async Task<EmailConfirmationSendResult> SendEmailConfirmationAsync(ApplicationUser user, string email)
    {
        if (string.IsNullOrWhiteSpace(email) || user.EmailConfirmed)
            return EmailConfirmationSendResult.AlreadyConfirmed;

        if (!_email.IsConfigured)
        {
            _logger.LogWarning(
                "Email confirmation not sent for user {UserId}: SMTP not configured ({Reason})",
                user.Id,
                SmtpEmailSettings.From(_config).DescribeReadiness());
            return EmailConfirmationSendResult.NotConfigured;
        }

        var token = await _users.GenerateEmailConfirmationTokenAsync(user);
        var urlToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = _urls.BuildPageUrl("confirm-email", new Dictionary<string, string?>
        {
            ["userId"] = user.Id,
            ["token"] = urlToken
        });

        var body = $"""
            <p>Hello {user.FullName},</p>
            <p>Thanks for registering with A to Z Clinical. Please confirm your email address to activate your account.</p>
            <p style="margin:24px 0">
              <a href="{link}" style="display:inline-block;padding:12px 24px;background:#0b4f8a;color:#fff;text-decoration:none;border-radius:6px;font-weight:600">
                Confirm your email
              </a>
            </p>
            <p style="color:#666;font-size:14px">Or open this link: <a href="{link}">{link}</a></p>
            <p style="color:#666;font-size:14px">If you did not register, you can ignore this message.</p>
            """;

        try
        {
            await _email.SendAsync(email.Trim(), "Confirm your email", body);
            _logger.LogInformation("Email confirmation sent to {Email} for user {UserId}", email, user.Id);
            return EmailConfirmationSendResult.Sent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email confirmation to {Email} for user {UserId}", email, user.Id);
            return EmailConfirmationSendResult.Failed;
        }
    }
}
