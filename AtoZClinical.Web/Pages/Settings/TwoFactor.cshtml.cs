using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Pages.Settings;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Settings;

public class TwoFactorModel : SettingsPageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly MfaPolicyService _mfaPolicy;
    private readonly IConfiguration _config;

    public TwoFactorModel(
        ClinicContextService clinicContext,
        ClinicSettingsService settingsService,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        MfaPolicyService mfaPolicy,
        IConfiguration config)
        : base(clinicContext, settingsService)
    {
        _users = users;
        _signIn = signIn;
        _mfaPolicy = mfaPolicy;
        _config = config;
    }

    public bool Is2faEnabled { get; private set; }
    public string? SharedKey { get; private set; }
    public string? AuthenticatorUri { get; private set; }
    public int RecoveryCodesLeft { get; private set; }
    public bool ShowSetup { get; private set; }
    public bool MfaRequired { get; private set; }
    public string? Message { get; private set; }

    [BindProperty]
    public string? VerificationCode { get; set; }

    public async Task<IActionResult> OnGetAsync(bool required = false)
    {
        await LoadClinicContextAsync();
        MfaRequired = required || _mfaPolicy.IsEnforcementEnabled;
        await LoadStatusAsync();
        if (!Is2faEnabled)
            await LoadAuthenticatorSetupAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostEnableAsync()
    {
        await LoadClinicContextAsync();
        var user = await _users.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Account/Login");

        if (string.IsNullOrWhiteSpace(VerificationCode))
        {
            ModelState.AddModelError(nameof(VerificationCode), "Enter the verification code.");
            await LoadStatusAsync();
            await LoadAuthenticatorSetupAsync();
            ShowSetup = true;
            return Page();
        }

        var code = VerificationCode.Replace(" ", "").Replace("-", "");
        var isValid = await _users.VerifyTwoFactorTokenAsync(
            user, _users.Options.Tokens.AuthenticatorTokenProvider, code);
        if (!isValid)
        {
            ModelState.AddModelError(nameof(VerificationCode), "Invalid verification code.");
            await LoadStatusAsync();
            await LoadAuthenticatorSetupAsync();
            ShowSetup = true;
            return Page();
        }

        await _users.SetTwoFactorEnabledAsync(user, true);
        if (await _users.CountRecoveryCodesAsync(user) == 0)
        {
            var codes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            TempData["RecoveryCodes"] = string.Join("\n", codes ?? Array.Empty<string>());
        }

        await _signIn.RefreshSignInAsync(user);
        Message = "Two-factor authentication enabled.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisableAsync()
    {
        await LoadClinicContextAsync();
        var user = await _users.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Account/Login");

        if (await _mfaPolicy.RequiresMfaAsync(user))
        {
            Message = "MFA is required for your account and cannot be disabled.";
            await LoadStatusAsync();
            return Page();
        }

        await _users.SetTwoFactorEnabledAsync(user, false);
        await _signIn.RefreshSignInAsync(user);
        Message = "Two-factor authentication disabled.";
        return RedirectToPage();
    }

    private async Task LoadStatusAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return;
        Is2faEnabled = user.TwoFactorEnabled;
        RecoveryCodesLeft = await _users.CountRecoveryCodesAsync(user);
        if (TempData["RecoveryCodes"] is string codes)
            Message = "Save these recovery codes in a safe place:\n" + codes;
    }

    private async Task LoadAuthenticatorSetupAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return;

        var unformattedKey = await _users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(unformattedKey))
        {
            await _users.ResetAuthenticatorKeyAsync(user);
            unformattedKey = await _users.GetAuthenticatorKeyAsync(user);
        }

        SharedKey = FormatKey(unformattedKey!);
        var issuer = _config["System:ProductName"] ?? "AtoZClinical";
        var email = user.Email ?? user.UserName ?? "user";
        AuthenticatorUri = GenerateQrUri(issuer, email, unformattedKey!);
        ShowSetup = true;
    }

    private static string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        for (var i = 0; i < unformattedKey.Length; i++)
        {
            result.Append(unformattedKey[i]);
            if ((i + 1) % 4 == 0 && i < unformattedKey.Length - 1)
                result.Append(' ');
        }
        return result.ToString().ToLowerInvariant();
    }

    private static string GenerateQrUri(string issuer, string email, string unformattedKey)
    {
        const string authenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";
        return string.Format(
            authenticatorUriFormat,
            UrlEncoder.Default.Encode(issuer),
            UrlEncoder.Default.Encode(email),
            unformattedKey);
    }
}
