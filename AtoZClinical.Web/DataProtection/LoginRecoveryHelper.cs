namespace AtoZClinical.Web.DataProtection;

public static class LoginRecoveryHelper
{
    public static bool IsLoginPath(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        return path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase);
    }

    public const string EmergencyLoginHtml =
        """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8"/>
            <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
            <title>Sign in - A to Z Clinical</title>
            <style>
                body { font-family: Segoe UI, sans-serif; max-width: 420px; margin: 2rem auto; padding: 1rem; }
                h2 { color: #0b4f8a; margin-bottom: 0.25rem; }
                label { display: block; margin-top: 0.75rem; font-weight: 600; }
                input[type=text], input[type=password] { width: 100%; padding: 0.5rem; margin-top: 0.25rem; box-sizing: border-box; }
                button { width: 100%; margin-top: 1rem; padding: 0.65rem; background: #0b4f8a; color: #fff; border: 0; cursor: pointer; }
                .note { color: #555; font-size: 0.9rem; margin-bottom: 1rem; }
            </style>
        </head>
        <body>
            <h2>A to Z Clinical</h2>
            <p class="note">Your session was reset. Sign in below.</p>
            <form method="post" action="/Account/Login">
                <label for="username">Username</label>
                <input id="username" name="Input.Username" autocomplete="username" required />
                <label for="password">Password</label>
                <input id="password" name="Input.Password" type="password" autocomplete="current-password" required />
                <label><input name="Input.RememberMe" type="checkbox" value="true" /> Remember me</label>
                <button type="submit">Login</button>
            </form>
        </body>
        </html>
        """;
}
