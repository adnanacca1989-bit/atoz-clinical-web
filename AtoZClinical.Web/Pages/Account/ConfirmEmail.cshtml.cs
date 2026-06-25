using System.Text;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace AtoZClinical.Web.Pages.Account;

public class ConfirmEmailModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;

    public ConfirmEmailModel(UserManager<ApplicationUser> users) => _users = users;

    public bool Succeeded { get; private set; }
    public string? Message { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? userId, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            Message = "Invalid confirmation link.";
            return Page();
        }

        var user = await _users.FindByIdAsync(userId);
        if (user is null)
        {
            Message = "User not found.";
            return Page();
        }

        try
        {
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            var result = await _users.ConfirmEmailAsync(user, token);
            if (result.Succeeded)
            {
                Succeeded = true;
                Message = "Thank you — your email has been confirmed. You can now sign in.";
            }
            else
            {
                Message = "Email confirmation failed. The link may have expired.";
            }
        }
        catch
        {
            Message = "Invalid or expired confirmation link.";
        }

        return Page();
    }
}
