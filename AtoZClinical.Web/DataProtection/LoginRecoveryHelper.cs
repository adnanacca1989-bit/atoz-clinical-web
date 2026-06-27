namespace AtoZClinical.Web.DataProtection;

public static class LoginRecoveryHelper
{
    public static bool IsLoginPath(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        return path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Clears stale cookies and either redirects to login or shows a simple retry page
    /// (avoids redirect loops that trigger HTTP 429 rate limits).
    /// </summary>
    public static async Task RecoverToLoginAsync(HttpContext context)
    {
        DataProtectionExceptionHelper.ClearProtectedCookies(context);

        if (context.Response.HasStarted)
            return;

        context.Response.Clear();

        if (IsLoginPath(context))
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(
                """
                <!DOCTYPE html>
                <html lang="en"><head><meta charset="utf-8"/><title>Sign in</title></head>
                <body style="font-family:Segoe UI,sans-serif;max-width:420px;margin:2rem auto;padding:1rem;">
                <h2>Session refreshed</h2>
                <p>Your browser cookies were cleared after an update. Please sign in again.</p>
                <p><a href="/Account/Login">Continue to sign in</a></p>
                </body></html>
                """);
            return;
        }

        context.Response.Redirect("/Account/Login");
    }
}
