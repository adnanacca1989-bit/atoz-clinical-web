using System.Text;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace AtoZClinical.Web.Services;

public sealed class RegistrationEmailService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IClinicalEmailSender _email;
    private readonly ClinicalAppUrls _urls;

    public RegistrationEmailService(
        UserManager<ApplicationUser> users,
        IClinicalEmailSender email,
        ClinicalAppUrls urls)
    {
        _users = users;
        _email = email;
        _urls = urls;
    }

    public async Task<bool> TrySendEmailConfirmationAsync(ApplicationUser user, string email)
    {
        if (string.IsNullOrWhiteSpace(email) || user.EmailConfirmed)
            return false;

        if (!_email.IsConfigured)
            return false;

        var token = await _users.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = _urls.BuildPageUrl("Account/ConfirmEmail", new Dictionary<string, string?>
        {
            ["userId"] = user.Id,
            ["code"] = code
        });

        var body = $"""
            <p>Hello {user.FullName},</p>
            <p>Welcome to A to Z Clinical. Please confirm your email address to activate your account.</p>
            <p><a href="{link}">Confirm my email</a></p>
            <p>If you did not register, you can ignore this message.</p>
            """;

        await _email.SendAsync(email, "Confirm your A to Z Clinical account", body);
        return true;
    }
}
