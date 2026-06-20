using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Settings;

public class ChangePasswordModel : SettingsPageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public ChangePasswordModel(
        ClinicContextService clinicContext,
        ClinicSettingsService settingsService,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
        : base(clinicContext, settingsService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [BindProperty] public string CurrentPassword { get; set; } = string.Empty;
    [BindProperty] public string NewPassword { get; set; } = string.Empty;
    [BindProperty] public string ConfirmPassword { get; set; } = string.Empty;
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync() => await LoadClinicContextAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadClinicContextAsync();

        if (NewPassword != ConfirmPassword)
        {
            ModelState.AddModelError(string.Empty, "New password and confirmation do not match.");
            return Page();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Account/Login");

        var result = await _userManager.ChangePasswordAsync(user, CurrentPassword, NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        await _signInManager.RefreshSignInAsync(user);
        StatusMessage = "Password changed successfully.";
        CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
        return Page();
    }
}
