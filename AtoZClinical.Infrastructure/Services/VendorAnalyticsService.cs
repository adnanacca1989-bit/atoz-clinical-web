using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Billing;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class VendorAnalyticsService
{
    private readonly ReportingDataService _reporting;

    public VendorAnalyticsService(ReportingDataService reporting) => _reporting = reporting;

    public async Task<VendorAnalyticsSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var _db = _reporting.ReadDb;
        var clinics = await _db.Clinics.AsNoTracking().ToListAsync(ct);
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var active = clinics.Count(c => c.Status == ClinicStatus.Active);
        var trial = clinics.Count(c =>
            c.PlanName.Equals(SubscriptionPlans.Trial, StringComparison.OrdinalIgnoreCase) &&
            c.Status == ClinicStatus.Active);
        var expired = clinics.Count(c => c.Status == ClinicStatus.Expired);
        var pending = clinics.Count(c => c.Status == ClinicStatus.Pending);
        var newThisMonth = clinics.Count(c => c.CreatedAt >= monthStart);
        var churnedThisMonth = clinics.Count(c =>
            c.Status == ClinicStatus.Expired && c.LicenseExpires >= monthStart);

        decimal mrr = 0m;
        foreach (var clinic in clinics.Where(c => c.Status == ClinicStatus.Active))
        {
            var plan = BillingPlanCatalog.FindByPlanName(clinic.PlanName);
            if (plan is not null && SubscriptionStatuses.IsPaid(clinic.SubscriptionStatus))
                mrr += plan.MonthlyPriceUsd;
        }

        var expiringTrials = clinics
            .Where(c => c.PlanName.Equals(SubscriptionPlans.Trial, StringComparison.OrdinalIgnoreCase))
            .Where(c => c.TrialEndsAt.HasValue && c.TrialEndsAt.Value.Date >= today)
            .OrderBy(c => c.TrialEndsAt)
            .Take(10)
            .Select(c => new TrialExpiringRow(c.Name, c.Email, c.TrialEndsAt!.Value))
            .ToList();

        return new VendorAnalyticsSummary(
            clinics.Count,
            active,
            trial,
            expired,
            pending,
            newThisMonth,
            churnedThisMonth,
            mrr,
            expiringTrials);
    }
}

public sealed record VendorAnalyticsSummary(
    int TotalClinics,
    int ActiveClinics,
    int TrialClinics,
    int ExpiredClinics,
    int PendingClinics,
    int NewClinicsThisMonth,
    int ChurnedThisMonth,
    decimal EstimatedMrrUsd,
    IReadOnlyList<TrialExpiringRow> ExpiringTrials);

public sealed record TrialExpiringRow(string ClinicName, string? Email, DateTime TrialEndsAt);

public sealed class ClinicDataDeletionService
{
    private readonly ClinicalDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ClinicRuntimeCache _cache;

    public ClinicDataDeletionService(
        ClinicalDbContext db,
        UserManager<ApplicationUser> users,
        ClinicRuntimeCache cache)
    {
        _db = db;
        _users = users;
        _cache = cache;
    }

    public async Task DeleteClinicAndAllDataAsync(Guid clinicId, CancellationToken ct = default)
    {
        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct)
            ?? throw new InvalidOperationException("Clinic not found.");

        var users = await _users.Users.Where(u => u.ClinicId == clinicId).ToListAsync(ct);
        foreach (var user in users)
        {
            var result = await _users.DeleteAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        _db.Clinics.Remove(clinic);
        await _db.SaveChangesAsync(ct);
        _cache.InvalidateClinic(clinicId);
    }
}
