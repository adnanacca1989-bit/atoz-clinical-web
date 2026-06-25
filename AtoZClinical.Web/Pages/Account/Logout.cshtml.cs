using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;
    private readonly SecurityAuditService _securityAudit;

    public LogoutModel(
        SignInManager<ApplicationUser> signIn,
        UserManager<ApplicationUser> users,
        SecurityAuditService securityAudit)
    {
        _signIn = signIn;
        _users = users;
        _securityAudit = securityAudit;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _users.GetUserAsync(User);
        var userName = user?.UserName;
        var clinicId = user?.ClinicId;

        await _signIn.SignOutAsync();

        if (!string.IsNullOrWhiteSpace(userName))
        {
            await _securityAudit.LogAsync(
                SecurityAuditEvents.Logout,
                userName,
                clinicId,
                "User signed out",
                HttpContext.Connection.RemoteIpAddress?.ToString());
        }

        return RedirectToPage("/Index");
    }
}
