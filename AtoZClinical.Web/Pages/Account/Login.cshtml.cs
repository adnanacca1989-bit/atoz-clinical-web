using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AtoZClinical.Web.Pages.Account;

[IgnoreAntiforgeryToken]
public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ClinicAccessService _access;
    private readonly ClinicalDbContext _db;
    private readonly SecurityAuditService _securityAudit;
    private readonly IClinicalEmailSender _email;

    public LoginModel(
        SignInManager<ApplicationUser> signIn,
        UserManager<ApplicationUser> users,
        ClinicAccessService access,
        ClinicalDbContext db,
        SecurityAuditService securityAudit,
        IClinicalEmailSender email)
    {
        _signIn = signIn;
        _users = users;
        _access = access;
        _db = db;
        _securityAudit = securityAudit;
        _email = email;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public bool ShowResendConfirmation { get; private set; }

    public void OnGet()
    {
    }

    [EnableRateLimiting("auth")]
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await _users.FindByNameAsync(Input.Username);
        if (user is null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return Page();
        }

        var result = await _signIn.PasswordSignInAsync(Input.Username, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.RequiresTwoFactor)
            return RedirectToPage("/Account/LoginWith2fa", new { RememberMe = Input.RememberMe });

        if (!result.Succeeded)
        {
            await _securityAudit.LogAsync(
                SecurityAuditEvents.LoginFailed,
                Input.Username,
                user?.ClinicId,
                "Invalid password or account locked.",
                HttpContext.Connection.RemoteIpAddress?.ToString());
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return Page();
        }

        if (!user.IsVendorAdmin
            && !await _users.IsInRoleAsync(user, ClinicalRoles.Vendor)
            && !string.IsNullOrWhiteSpace(user.Email)
            && !user.EmailConfirmed)
        {
            if (!_email.IsConfigured)
            {
                user.EmailConfirmed = true;
                await _users.UpdateAsync(user);
            }
            else
            {
                await _signIn.SignOutAsync();
                ShowResendConfirmation = true;
                ModelState.AddModelError(string.Empty,
                    "Please confirm your email before signing in. Check your inbox for the confirmation link.");
                return Page();
            }
        }

        if (user.IsVendorAdmin || await _users.IsInRoleAsync(user, ClinicalRoles.Vendor))
        {
            await _securityAudit.LogAsync(
                SecurityAuditEvents.Login,
                user.UserName,
                null,
                "Vendor login",
                HttpContext.Connection.RemoteIpAddress?.ToString());
            return RedirectToPage("/Vendor/Dashboard");
        }

        Clinic? clinic = null;
        if (user.ClinicId.HasValue)
            clinic = await _db.Clinics.FindAsync(user.ClinicId.Value);

        var access = _access.Evaluate(clinic);
        if (!access.IsAllowed)
        {
            await _signIn.SignOutAsync();
            return RedirectToPage("/Account/LicenseBlocked", new { reason = (int)access.Reason });
        }

        await _securityAudit.LogAsync(
            SecurityAuditEvents.Login,
            user.UserName,
            user.ClinicId,
            $"Clinic login: {clinic?.Name}",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return RedirectToPage("/Dashboard/Index");
    }

    public sealed class LoginInput
    {
        [Required, Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }
}
