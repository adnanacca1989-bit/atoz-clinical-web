using System.Text;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Web.Services;

public sealed class RegistrationEmailService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IClinicalEmailSender _email;
    private readonly ClinicalAppUrls _urls;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<RegistrationEmailService> _logger;

    public RegistrationEmailService(
        UserManager<ApplicationUser> users,
        IClinicalEmailSender email,
        ClinicalAppUrls urls,
        IConfiguration config,
        IHostEnvironment env,
        ILogger<RegistrationEmailService> logger)
    {
        _users = users;
        _email = email;
        _urls = urls;
        _config = config;
        _env = env;
        _logger = logger;
    }

    public async Task<EmailConfirmationSendResult> SendEmailConfirmationAsync(ApplicationUser user, string email)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email) || user.EmailConfirmed)
                return EmailConfirmationSendResult.AlreadyConfirmed;

            if (!SmtpEmailConfiguration.IsEmailConfigured(_config))
            {
                var missing = SmtpEmailConfiguration.GetMissingVariables(_config);
                _logger.LogWarning(
                    "Email skipped (not configured) for user {UserId}. Missing: {Missing}",
                    user.Id,
                    string.Join(", ", missing));

                if (_env.IsDevelopment())
                {
                    try
                    {
                        var devLink = await BuildConfirmationLinkAsync(user);
                        _logger.LogWarning("Development confirmation link for {Email}: {Link}", email.Trim(), devLink);
                    }
                    catch (Exception linkEx)
                    {
                        _logger.LogError(linkEx, "Could not build development confirmation link for user {UserId}", user.Id);
                    }
                }

                return EmailConfirmationSendResult.NotConfigured;
            }

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
            if (result.Skipped)
            {
                _logger.LogWarning(
                    "Email confirmation skipped (not configured) for user {UserId}. Missing: {Missing}",
                    user.Id,
                    string.Join(", ", SmtpEmailConfiguration.GetMissingVariables(_config)));
                return EmailConfirmationSendResult.NotConfigured;
            }

            if (!result.Success)
            {
                _logger.LogError(
                    "Failed to send email confirmation to {Email} for user {UserId}: {Reason}",
                    email, user.Id, result.Message);
                return EmailConfirmationSendResult.Failed;
            }

            _logger.LogInformation("Email confirmation sent to {Email} for user {UserId}", email, user.Id);
            return EmailConfirmationSendResult.Sent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email confirmation failed for user {UserId} email {Email}", user.Id, email);
            return EmailConfirmationSendResult.Failed;
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
