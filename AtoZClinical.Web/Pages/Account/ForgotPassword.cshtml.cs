using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;

    public ForgotPasswordModel(UserManager<ApplicationUser> users) => _users = users;

    [BindProperty]
    public ForgotInput Input { get; set; } = new();

    public bool Submitted { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        _ = await _users.FindByNameAsync(Input.Username.Trim());
        Submitted = true;
        return Page();
    }

    public sealed class ForgotInput
    {
        [Required, Display(Name = "Username or Email")]
        public string Username { get; set; } = string.Empty;
    }
}
