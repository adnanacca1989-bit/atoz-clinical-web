using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ClinicAccessService _access;
    private readonly ClinicalDbContext _db;

    public LoginModel(
        SignInManager<ApplicationUser> signIn,
        UserManager<ApplicationUser> users,
        ClinicAccessService access,
        ClinicalDbContext db)
    {
        _signIn = signIn;
        _users = users;
        _access = access;
        _db = db;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await _users.FindByNameAsync(Input.Username);
        if (user is null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return Page();
        }

        var result = await _signIn.PasswordSignInAsync(Input.Username, Input.Password, Input.RememberMe, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return Page();
        }

        if (user.IsVendorAdmin || await _users.IsInRoleAsync(user, ClinicalRoles.Vendor))
            return RedirectToPage("/Vendor/Index");

        Clinic? clinic = null;
        if (user.ClinicId.HasValue)
            clinic = await _db.Clinics.FindAsync(user.ClinicId.Value);

        var access = _access.Evaluate(clinic);
        if (!access.IsAllowed)
        {
            await _signIn.SignOutAsync();
            return RedirectToPage("/Account/LicenseBlocked", new { reason = (int)access.Reason });
        }

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
