using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;

namespace AtoZClinical.Web.DataProtection;

public static class DataProtectionExceptionHelper
{
    public static bool IsRecoverable(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is AntiforgeryValidationException or CryptographicException or KeyNotFoundException)
                return true;

            var msg = ex.Message;
            if (msg.Contains("key ring", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("key was not found", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("could not be decrypted", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Unprotect", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("antiforgery", StringComparison.OrdinalIgnoreCase))
                return true;

            ex = ex.InnerException;
        }

        return false;
    }

    public static bool IsProtectedCookie(string name) =>
        name.StartsWith(".AspNetCore.", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("__Clinical", StringComparison.OrdinalIgnoreCase)
        || name.Equals("atz_patient_portal", StringComparison.OrdinalIgnoreCase);

    public static void ClearProtectedCookies(HttpContext context)
    {
        foreach (var cookie in context.Request.Cookies.Keys)
        {
            if (IsProtectedCookie(cookie))
                DeleteCookie(context, cookie);
        }
    }

    public static void ClearProtectedCookiesForNextRequest(HttpContext context)
    {
        foreach (var cookie in context.Request.Cookies.Keys)
        {
            if (IsProtectedCookie(cookie))
                DeleteCookie(context, cookie);
        }
    }

    private static void DeleteCookie(HttpContext context, string cookie)
    {
        context.Response.Cookies.Delete(cookie, BuildDeleteOptions(context));

        // Identity may set chunked cookies (.C1, .C2, …).
        for (var i = 0; i < 5; i++)
            context.Response.Cookies.Delete($"{cookie}C{i}", BuildDeleteOptions(context));
    }

    private static CookieOptions BuildDeleteOptions(HttpContext context) =>
        new()
        {
            Path = "/",
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            HttpOnly = true,
            Expires = DateTimeOffset.UnixEpoch
        };
}
