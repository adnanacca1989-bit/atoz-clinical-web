namespace AtoZClinical.Web.Middleware;

public static class ClientIpHelper
{
    public static string GetClientIp(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded)
            && !string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (first.Length > 0)
                return first[0];
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
