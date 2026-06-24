namespace AtoZClinical.Web.Middleware;

public sealed class LanguageMiddleware
{
    private const string CookieName = "clinical_lang";
    private readonly RequestDelegate _next;

    public LanguageMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Query.TryGetValue("lang", out var langValues))
        {
            var lang = langValues.ToString().Trim().ToLowerInvariant();
            if (lang is "en" or "ar")
            {
                context.Response.Cookies.Append(CookieName, lang, new CookieOptions
                {
                    HttpOnly = false,
                    IsEssential = true,
                    MaxAge = TimeSpan.FromDays(365),
                    SameSite = SameSiteMode.Lax
                });
                context.Items[CookieName] = lang;

                var path = context.Request.Path.Value ?? "/";
                var query = context.Request.Query
                    .Where(q => !string.Equals(q.Key, "lang", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(q => q.Value.Select(v => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(v ?? "")}"));
                var qs = string.Join("&", query);
                context.Response.Redirect(string.IsNullOrEmpty(qs) ? path : $"{path}?{qs}");
                return;
            }
        }

        context.Items[CookieName] = context.Request.Cookies[CookieName] ?? "en";
        await _next(context);
    }

    public static string GetLanguage(HttpContext context) =>
        context.Items[CookieName] as string ?? context.Request.Cookies[CookieName] ?? "en";
}
