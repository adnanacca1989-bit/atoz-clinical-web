using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AtoZClinical.Infrastructure.Identity;

namespace AtoZClinical.Web.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signIn;

    public LogoutModel(SignInManager<ApplicationUser> signIn) => _signIn = signIn;

    public async Task<IActionResult> OnPostAsync()
    {
        await _signIn.SignOutAsync();
        return RedirectToPage("/Index");
    }
}
