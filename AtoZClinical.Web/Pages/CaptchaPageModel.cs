using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages;

public abstract class CaptchaPageModel : PageModel
{
    [BindProperty]
    public string? CaptchaToken { get; set; }

    protected string? GetCaptchaResponseToken() =>
        CaptchaToken
        ?? Request.Form["h-captcha-response"].FirstOrDefault()
        ?? Request.Form["g-recaptcha-response"].FirstOrDefault();

    protected async Task<bool> ValidateCaptchaAsync(CaptchaService captcha)
    {
        if (!captcha.IsEnabled)
            return true;

        if (await captcha.ValidateAsync(GetCaptchaResponseToken(), HttpContext.Connection.RemoteIpAddress?.ToString()))
            return true;

        ModelState.AddModelError(string.Empty, "CAPTCHA verification failed. Please try again.");
        return false;
    }
}
