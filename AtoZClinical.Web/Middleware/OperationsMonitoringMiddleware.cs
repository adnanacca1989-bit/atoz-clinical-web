using System.Net.Http.Json;
using AtoZClinical.Web.Services;

namespace AtoZClinical.Web.Middleware;

public sealed class OperationsMonitoringMiddleware
{
    private readonly RequestDelegate _next;
    private readonly OperationalMetrics _metrics;
    private readonly IConfiguration _config;
    private readonly ILogger<OperationsMonitoringMiddleware> _logger;
    private readonly HttpClient _http;
    private DateTime _lastAlertUtc = DateTime.MinValue;

    public OperationsMonitoringMiddleware(
        RequestDelegate next,
        OperationalMetrics metrics,
        IConfiguration config,
        ILogger<OperationsMonitoringMiddleware> logger,
        IHttpClientFactory httpClientFactory)
    {
        _next = next;
        _metrics = metrics;
        _config = config;
        _logger = logger;
        _http = httpClientFactory.CreateClient("operations-alerts");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);
        _metrics.Record(context.Response.StatusCode);

        if (context.Response.StatusCode < 500)
            return;

        _logger.LogError(
            "Server error {StatusCode} on {Method} {Path} trace={TraceId}",
            context.Response.StatusCode,
            context.Request.Method,
            context.Request.Path,
            context.TraceIdentifier);

        await TrySendAlertAsync(context);
    }

    private async Task TrySendAlertAsync(HttpContext context)
    {
        var webhook = _config["Operations:AlertWebhookUrl"];
        if (string.IsNullOrWhiteSpace(webhook))
            return;

        var threshold = _config.GetValue("Operations:AlertErrorRatePercent", 5.0);
        var minRequests = _config.GetValue("Operations:AlertMinRequests", 20);
        var cooldownMinutes = _config.GetValue("Operations:AlertCooldownMinutes", 15);

        var snapshot = _metrics.GetSnapshot();
        if (snapshot.TotalRequests < minRequests || snapshot.ServerErrorRatePercent < threshold)
            return;

        if ((DateTime.UtcNow - _lastAlertUtc).TotalMinutes < cooldownMinutes)
            return;

        _lastAlertUtc = DateTime.UtcNow;

        try
        {
            var payload = new
            {
                text = $"AtoZ Clinical alert: {snapshot.ServerErrors} server errors " +
                       $"({snapshot.ServerErrorRatePercent}% of {snapshot.TotalRequests} requests). " +
                       $"Last path: {context.Request.Method} {context.Request.Path}"
            };
            await _http.PostAsJsonAsync(webhook, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to post operations alert webhook.");
        }
    }
}
