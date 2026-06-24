using AtoZClinical.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IConfiguration _config;

    public IndexModel(IConfiguration config) => _config = config;

    public string? PublicUrl { get; private set; }
    public string TrialRegistrationUrl { get; private set; } = "";
    public bool IsLocalOnly { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return User.IsInRole(ClinicalRoles.Vendor)
                ? RedirectToPage("/Vendor/Index")
                : RedirectToPage("/Dashboard/Index");
        }

        var configured = _config["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
        var host = HttpContext.Request.Host.Host;
        IsLocalOnly = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);

        var baseUrl = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : IsLocalOnly
                ? $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}"
                : $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";

        PublicUrl = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : IsLocalOnly
                ? null
                : baseUrl;

        TrialRegistrationUrl = $"{baseUrl.TrimEnd('/')}/Register/Trial";

        return Page();
    }
}
