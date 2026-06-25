using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Portal;

public class LogoutModel : PageModel
{
    private readonly PatientPortalSession _session;

    public LogoutModel(PatientPortalSession session) => _session = session;

    public IActionResult OnGet()
    {
        _session.SignOut(HttpContext);
        return RedirectToPage("/Portal/Login");
    }
}
