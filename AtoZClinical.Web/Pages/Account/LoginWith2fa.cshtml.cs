using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AtoZClinical.Web.Pages.Account;

[IgnoreAntiforgeryToken]
public class LoginWith2faModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signIn;

    public LoginWith2faModel(SignInManager<ApplicationUser> signIn) => _signIn = signIn;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public bool RememberMe { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet()
    {
    }

    [EnableRateLimiting("auth")]
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var code = Input.TwoFactorCode.Replace(" ", "").Replace("-", "");
        var result = await _signIn.TwoFactorAuthenticatorSignInAsync(code, RememberMe, Input.RememberMachine);
        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                return LocalRedirect(ReturnUrl);
            return RedirectToPage("/Dashboard/Index");
        }

        ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
        return Page();
    }

    public sealed class InputModel
    {
        [Required, Display(Name = "Authenticator code")]
        public string TwoFactorCode { get; set; } = string.Empty;

        [Display(Name = "Remember this machine")]
        public bool RememberMachine { get; set; }
    }
}
