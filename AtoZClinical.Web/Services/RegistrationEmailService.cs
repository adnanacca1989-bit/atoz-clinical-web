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

    public async Task<EmailConfirmationSendOutcome> SendEmailConfirmationAsync(ApplicationUser user, string email)
    {
        if (string.IsNullOrWhiteSpace(email) || user.EmailConfirmed)
            return EmailConfirmationSendOutcome.AlreadyConfirmed();

        if (!SmtpEmailConfiguration.IsEmailConfigured(_config))
        {
            var missing = SmtpEmailConfiguration.GetMissingVariables(_config);
            _logger.LogError(
                "Cannot send confirmation email to {Email} for user {UserId}: SMTP not configured. Missing: {Missing}",
                email, user.Id, string.Join(", ", missing));
            throw new ClinicalEmailNotConfiguredException(missing);
        }

        try
        {
            var link = await BuildConfirmationLinkAsync(user);
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

            var result = await _email.SendAsync(email.Trim(), "Confirm your email", body);
            if (!result.Success)
            {
                _logger.LogError(
                    "Confirmation email failed for {Email} user {UserId}: {Reason}",
                    email, user.Id, result.Message);
                return EmailConfirmationSendOutcome.Failed(result.Message);
            }

            _logger.LogInformation(
                "Confirmation email sent successfully to {Email} for user {UserId}",
                email, user.Id);
            return EmailConfirmationSendOutcome.Sent();
        }
        catch (ClinicalEmailNotConfiguredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Confirmation email failed for {Email} user {UserId}", email, user.Id);
            return EmailConfirmationSendOutcome.Failed(SmtpEmailDiagnostics.ClassifyFailure(ex));
        }
    }

    private async Task<string> BuildConfirmationLinkAsync(ApplicationUser user)
    {
        var token = await _users.GenerateEmailConfirmationTokenAsync(user);
        var urlToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return _urls.BuildPageUrl("confirm-email", new Dictionary<string, string?>
        {
            ["userId"] = user.Id,
            ["token"] = urlToken
        });
    }
}
