using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Account;

public class ExternalLoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ClinicAccessService _access;
    private readonly IConfiguration _config;

    public ExternalLoginModel(
        SignInManager<ApplicationUser> signIn,
        UserManager<ApplicationUser> users,
        ClinicAccessService access,
        IConfiguration config)
    {
        _signIn = signIn;
        _users = users;
        _access = access;
        _config = config;
    }

    public IActionResult OnGet() => RedirectToPage("/Account/Login");

    public IActionResult OnPost(string provider, string? returnUrl = null)
    {
        if (!IsProviderConfigured(provider))
            return RedirectToPage("/Account/Login");

        var redirectUrl = Url.Page("/Account/ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
        var properties = _signIn.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return new ChallengeResult(provider, properties);
    }

    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        if (!string.IsNullOrEmpty(remoteError))
            return RedirectToPage("/Account/Login");

        var info = await _signIn.GetExternalLoginInfoAsync();
        if (info is null)
            return RedirectToPage("/Account/Login");

        var email = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email))
            return RedirectToPage("/Account/Login");

        var user = await _users.FindByEmailAsync(email);
        if (user is null || !user.IsActive)
            return RedirectToPage("/Account/Login");

        var loginResult = await _signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false);
        if (!loginResult.Succeeded)
        {
            var addLogin = await _users.AddLoginAsync(user, info);
            if (!addLogin.Succeeded)
                return RedirectToPage("/Account/Login");

            await _signIn.SignInAsync(user, isPersistent: false);
        }

        if (user.IsVendorAdmin || await _users.IsInRoleAsync(user, ClinicalRoles.Vendor))
            return RedirectToPage("/Vendor/Index");

        if (user.ClinicId.HasValue)
        {
            var clinic = await HttpContext.RequestServices
                .GetRequiredService<Infrastructure.Data.ClinicalDbContext>()
                .Clinics.FindAsync(user.ClinicId.Value);
            var access = _access.Evaluate(clinic);
            if (!access.IsAllowed)
            {
                await _signIn.SignOutAsync();
                return RedirectToPage("/Account/LicenseBlocked", new { reason = (int)access.Reason });
            }
        }

        return RedirectToPage("/Dashboard/Index");
    }

    private bool IsProviderConfigured(string provider) => provider switch
    {
        "Google" => !string.IsNullOrWhiteSpace(_config["Authentication:Google:ClientId"]),
        "Microsoft" => !string.IsNullOrWhiteSpace(_config["Authentication:Microsoft:ClientId"]),
        _ => false
    };
}
