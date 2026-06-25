using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

public interface IWebhookDispatchService
{
    Task DispatchAsync(Guid clinicId, string eventName, object payload, CancellationToken cancellationToken = default);
}

public sealed class WebhookDispatchService : IWebhookDispatchService
{
    private readonly ClinicalDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookDispatchService> _logger;

    public WebhookDispatchService(
        ClinicalDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookDispatchService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task DispatchAsync(Guid clinicId, string eventName, object payload, CancellationToken cancellationToken = default)
    {
        var subscriptions = await _db.WebhookSubscriptions
            .AsNoTracking()
            .Where(w => w.ClinicId == clinicId && w.IsActive)
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0) return;

        var body = JsonSerializer.Serialize(new
        {
            @event = eventName,
            clinicId,
            timestamp = DateTime.UtcNow,
            data = payload
        });

        var client = _httpClientFactory.CreateClient("webhooks");
        foreach (var sub in subscriptions)
        {
            if (!IsSubscribed(sub.Events, eventName)) continue;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, sub.TargetUrl);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                request.Headers.Add("X-AtoZ-Event", eventName);
                request.Headers.Add("X-AtoZ-Signature", Sign(body, sub.Secret));
                var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    _logger.LogWarning("Webhook {Url} returned {Status} for {Event}", sub.TargetUrl, response.StatusCode, eventName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Webhook delivery failed for {Url} event {Event}", sub.TargetUrl, eventName);
            }
        }
    }

    private static bool IsSubscribed(string eventsCsv, string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventsCsv)) return false;
        return eventsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(e => string.Equals(e, eventName, StringComparison.OrdinalIgnoreCase));
    }

    private static string Sign(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class NoOpWebhookDispatchService : IWebhookDispatchService
{
    public Task DispatchAsync(Guid clinicId, string eventName, object payload, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
