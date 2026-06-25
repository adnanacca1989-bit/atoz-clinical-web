using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Webhooks;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class WebhookSubscriptionService
{
    private readonly ClinicalDbContext _db;

    public WebhookSubscriptionService(ClinicalDbContext db) => _db = db;

    public Task<List<WebhookSubscription>> ListAsync(Guid clinicId) =>
        _db.WebhookSubscriptions
            .Where(w => w.ClinicId == clinicId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();

    public async Task<WebhookSubscription> CreateAsync(Guid clinicId, string targetUrl, string eventsCsv)
    {
        var sub = new WebhookSubscription
        {
            ClinicId = clinicId,
            TargetUrl = targetUrl.Trim(),
            Events = eventsCsv.Trim(),
            Secret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()
        };
        _db.WebhookSubscriptions.Add(sub);
        await _db.SaveChangesAsync();
        return sub;
    }

    public async Task<bool> DeleteAsync(Guid clinicId, Guid id)
    {
        var sub = await _db.WebhookSubscriptions.FirstOrDefaultAsync(w => w.ClinicId == clinicId && w.Id == id);
        if (sub is null) return false;
        _db.WebhookSubscriptions.Remove(sub);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetActiveAsync(Guid clinicId, Guid id, bool isActive)
    {
        var sub = await _db.WebhookSubscriptions.FirstOrDefaultAsync(w => w.ClinicId == clinicId && w.Id == id);
        if (sub is null) return false;
        sub.IsActive = isActive;
        await _db.SaveChangesAsync();
        return true;
    }
}
