using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;

namespace AtoZClinical.Web.Services;

public sealed class ClinicalAppUrls
{
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _http;

    public ClinicalAppUrls(IConfiguration config, IHttpContextAccessor http)
    {
        _config = config;
        _http = http;
    }

    public string GetBaseUrl()
    {
        var configured = _config["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var request = _http.HttpContext?.Request;
        if (request is null)
            return "https://localhost";

        return $"{request.Scheme}://{request.Host}";
    }

    public string BuildPageUrl(string pagePath, IDictionary<string, string?>? query = null)
    {
        var path = pagePath.TrimStart('/');
        if (!path.StartsWith("Account/", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains('/'))
            path = pagePath;

        var basePath = $"/{path}";
        if (query is null || query.Count == 0)
            return $"{GetBaseUrl()}{basePath}";

        var qb = new QueryBuilder();
        foreach (var (key, value) in query)
        {
            if (!string.IsNullOrWhiteSpace(value))
                qb.Add(key, value);
        }

        return $"{GetBaseUrl()}{basePath}{qb.ToQueryString()}";
    }
}
