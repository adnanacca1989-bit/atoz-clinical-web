using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Filters;
using AtoZClinical.Web.Middleware;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AtoZClinical.Web.Pages.Account;

[IgnoreAntiforgeryToken]
[DisableRateLimiting]
public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ClinicAccessService _access;
    private readonly ClinicalDbContext _db;
    private readonly SecurityAuditService _securityAudit;
    private readonly FormPermissionService _permissions;
    private readonly ClinicRuntimeCache _runtimeCache;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        SignInManager<ApplicationUser> signIn,
        UserManager<ApplicationUser> users,
        ClinicAccessService access,
        ClinicalDbContext db,
        SecurityAuditService securityAudit,
        FormPermissionService permissions,
        ClinicRuntimeCache runtimeCache,
        ILogger<LoginModel> logger)
    {
        _signIn = signIn;
        _users = users;
        _access = access;
        _db = db;
        _securityAudit = securityAudit;
        _permissions = permissions;
        _runtimeCache = runtimeCache;
        _logger = logger;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public bool ShowResendConfirmation { get; private set; }

    public IActionResult OnGet(string? recovered)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole(ClinicalRoles.Vendor))
                return RedirectToPage("/Vendor/Dashboard");
            return RedirectToPage("/Dashboard/Index");
        }

        Input.Password = string.Empty;

        if (string.Equals(recovered, "1", StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty,
                "Your browser session was reset. Please sign in again.");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var clientIp = ClientIpHelper.GetClientIp(HttpContext);

        try
        {
            return await OnPostCoreAsync(clientIp, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled login error for user {Username} client={ClientIp} trace={TraceId}",
                Input.Username,
                clientIp,
                HttpContext.TraceIdentifier);

            try { await _signIn.SignOutAsync(); } catch { /* ignore */ }

            ModelState.AddModelError(string.Empty,
                "Sign-in could not be completed due to a system error. Please try again. If the problem continues, contact your clinic admin.");
            Input.Password = string.Empty;
            return Page();
        }
    }

    private async Task<IActionResult> OnPostCoreAsync(string clientIp, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Login POST received for user {Username} client={ClientIp} trace={TraceId}",
            Input.Username,
            clientIp,
            HttpContext.TraceIdentifier);

        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Login POST validation failed for user {Username} client={ClientIp}", Input.Username, clientIp);
            Input.Password = string.Empty;
            return Page();
        }

        var user = await _users.FindByNameAsync(Input.Username);
        if (user is null || !user.IsActive)
        {
            _logger.LogWarning("Login failed: unknown or inactive user {Username} client={ClientIp}", Input.Username, clientIp);
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            Input.Password = string.Empty;
            return Page();
        }

        _logger.LogInformation(
            "Login password check starting for {Username} userId={UserId} client={ClientIp} trace={TraceId}",
            Input.Username,
            user.Id,
            clientIp,
            HttpContext.TraceIdentifier);

        var result = await _signIn.PasswordSignInAsync(Input.Username, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        _logger.LogInformation(
            "PasswordSignInAsync result for {Username} succeeded={Succeeded} requires2fa={Requires2fa} lockedOut={LockedOut} notAllowed={NotAllowed} client={ClientIp} trace={TraceId}",
            Input.Username,
            result.Succeeded,
            result.RequiresTwoFactor,
            result.IsLockedOut,
            result.IsNotAllowed,
            clientIp,
            HttpContext.TraceIdentifier);

        if (result.RequiresTwoFactor)
            return RedirectToPage("/Account/LoginWith2fa", new { RememberMe = Input.RememberMe });

        if (result.IsNotAllowed)
        {
            ShowResendConfirmation = !string.IsNullOrWhiteSpace(user.Email);
            ModelState.AddModelError(string.Empty, "Please confirm your email before logging in.");
            Input.Password = string.Empty;
            return Page();
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Login failed for {Username} client={ClientIp} lockedOut={LockedOut} notAllowed={NotAllowed}",
                Input.Username,
                clientIp,
                result.IsLockedOut,
                result.IsNotAllowed);

            await _securityAudit.LogAsync(
                SecurityAuditEvents.LoginFailed,
                Input.Username,
                user?.ClinicId,
                "Invalid password or account locked.",
                HttpContext.Connection.RemoteIpAddress?.ToString());
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            Input.Password = string.Empty;
            return Page();
        }

        if (user.IsVendorAdmin || await _users.IsInRoleAsync(user, ClinicalRoles.Vendor))
        {
            _logger.LogInformation(
                "Vendor login succeeded for {Username} client={ClientIp} authenticated={Authenticated} trace={TraceId}",
                user.UserName,
                clientIp,
                User.Identity?.IsAuthenticated == true,
                HttpContext.TraceIdentifier);
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

        if (user.ClinicRole == ClinicUserRole.Doctor && user.DoctorRecordId is null)
        {
            await _signIn.SignOutAsync();
            _logger.LogWarning(
                "Doctor login blocked for {Username}: no DoctorRecordId linked clinicId={ClinicId}",
                user.UserName, user.ClinicId);
            await _securityAudit.LogAsync(
                SecurityAuditEvents.LoginFailed,
                user.UserName,
                user.ClinicId,
                "Doctor user not linked to a doctor record.",
                clientIp);
            ModelState.AddModelError(string.Empty,
                "Your doctor account is not linked to a doctor record. Ask your clinic admin to link you in Settings → Define User.");
            Input.Password = string.Empty;
            return Page();
        }

        if (user.ClinicId is Guid clinicId)
        {
            HttpContext.Items[HttpContextClinicProvider.TenantClinicIdKey] = clinicId;

            var responsibilityRole = _permissions.ResolveResponsibilityRole(user);
            _runtimeCache.InvalidateVisibleForms(clinicId, responsibilityRole);

            var visibleForms = await _permissions.GetVisibleFormsAsync(clinicId, responsibilityRole);
            if (visibleForms.Count == 0)
            {
                await _signIn.SignOutAsync();
                _logger.LogWarning(
                    "Login blocked for {Username}: role {Role} has no visible permissions clinicId={ClinicId}",
                    user.UserName, responsibilityRole, clinicId);
                await _securityAudit.LogAsync(
                    SecurityAuditEvents.LoginFailed,
                    user.UserName,
                    clinicId,
                    $"No permissions for role {responsibilityRole}.",
                    clientIp);
                ModelState.AddModelError(string.Empty,
                    $"No permissions assigned to role \"{responsibilityRole}\". Ask your clinic admin to open Responsibilities, select this role, check Dashboard, and click Save.");
                Input.Password = string.Empty;
                return Page();
            }
        }
        else
        {
            await _signIn.SignOutAsync();
            _logger.LogWarning("Login blocked for {Username}: clinic not assigned.", user.UserName);
            ModelState.AddModelError(string.Empty,
                "Your account is not assigned to a clinic. Contact your clinic admin.");
            Input.Password = string.Empty;
            return Page();
        }

        _logger.LogInformation(
            "Clinic login succeeded for {Username} clinicId={ClinicId} role={Role} doctorRecordId={DoctorRecordId} client={ClientIp} authenticated={Authenticated} trace={TraceId}",
            user.UserName,
            user.ClinicId,
            user.ClinicRole,
            user.DoctorRecordId,
            clientIp,
            User.Identity?.IsAuthenticated == true,
            HttpContext.TraceIdentifier);

        await _securityAudit.LogAsync(
            SecurityAuditEvents.Login,
            user.UserName,
            user.ClinicId,
            $"Clinic login: {clinic?.Name} role={user.ClinicRole} doctorRecordId={user.DoctorRecordId}",
            clientIp);

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
