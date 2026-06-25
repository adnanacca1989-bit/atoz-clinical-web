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
    private readonly UserManager<ApplicationUser> _users;

    public VendorAnalyticsService(ReportingDataService reporting, UserManager<ApplicationUser> users)
    {
        _reporting = reporting;
        _users = users;
    }

    public async Task<SaasDashboardSummary> GetDashboardAsync(CancellationToken ct = default)
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
        var suspended = clinics.Count(c => c.Status == ClinicStatus.Suspended);
        var newThisMonth = clinics.Count(c => c.CreatedAt >= monthStart);
        var churnedThisMonth = clinics.Count(c =>
            c.Status == ClinicStatus.Expired &&
            (c.SubscriptionExpiryDate >= monthStart || c.LicenseExpires >= monthStart));

        var totalUsers = await _users.Users.CountAsync(u => u.ClinicId != null && !u.IsVendorAdmin, ct);
        var totalPatients = await _db.Patients.IgnoreQueryFilters().CountAsync(ct);

        decimal mrr = 0m;
        foreach (var clinic in clinics.Where(c => c.Status == ClinicStatus.Active))
        {
            var plan = BillingPlanCatalog.FindByPlanName(clinic.PlanName);
            if (plan is not null && SubscriptionStatuses.IsPaid(clinic.SubscriptionStatus))
                mrr += plan.MonthlyPriceUsd;
        }

        var monthlyGrowth = await BuildMonthlyGrowthAsync(_db, ct);
        var planBreakdown = clinics
            .GroupBy(c => c.PlanName)
            .Select(g => new PlanCountRow(g.Key, g.Count()))
            .OrderByDescending(r => r.Count)
            .ToList();

        var expiringTrials = clinics
            .Where(c => c.PlanName.Equals(SubscriptionPlans.Trial, StringComparison.OrdinalIgnoreCase))
            .Where(c => c.TrialEndsAt.HasValue && c.TrialEndsAt.Value.Date >= today)
            .OrderBy(c => c.TrialEndsAt)
            .Take(10)
            .Select(c => new TrialExpiringRow(c.Name, c.Email, c.TrialEndsAt!.Value))
            .ToList();

        return new SaasDashboardSummary(
            clinics.Count,
            active,
            trial,
            expired,
            pending,
            suspended,
            newThisMonth,
            churnedThisMonth,
            totalUsers,
            totalPatients,
            mrr,
            mrr * 12m,
            monthlyGrowth,
            planBreakdown,
            expiringTrials);
    }

    public async Task<VendorAnalyticsSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var d = await GetDashboardAsync(ct);
        return new VendorAnalyticsSummary(
            d.TotalClinics,
            d.ActiveClinics,
            d.TrialClinics,
            d.ExpiredClinics,
            d.PendingClinics,
            d.NewClinicsThisMonth,
            d.ChurnedThisMonth,
            d.MonthlyRevenueUsd,
            d.ExpiringTrials);
    }

    private static async Task<IReadOnlyList<MonthlyGrowthRow>> BuildMonthlyGrowthAsync(
        ClinicalDbContext db,
        CancellationToken ct)
    {
        var sixMonthsAgo = DateTime.UtcNow.Date.AddMonths(-5);
        sixMonthsAgo = new DateTime(sixMonthsAgo.Year, sixMonthsAgo.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var created = await db.Clinics.AsNoTracking()
            .Where(c => c.CreatedAt >= sixMonthsAgo)
            .Select(c => c.CreatedAt)
            .ToListAsync(ct);

        var rows = new List<MonthlyGrowthRow>();
        for (var i = 0; i < 6; i++)
        {
            var month = sixMonthsAgo.AddMonths(i);
            var next = month.AddMonths(1);
            var count = created.Count(d => d >= month && d < next);
            rows.Add(new MonthlyGrowthRow(month.ToString("MMM yyyy"), count));
        }

        return rows;
    }
}

public sealed record SaasDashboardSummary(
    int TotalClinics,
    int ActiveClinics,
    int TrialClinics,
    int ExpiredClinics,
    int PendingClinics,
    int SuspendedClinics,
    int NewClinicsThisMonth,
    int ChurnedThisMonth,
    int TotalUsers,
    int TotalPatients,
    decimal MonthlyRevenueUsd,
    decimal AnnualRevenueUsd,
    IReadOnlyList<MonthlyGrowthRow> MonthlyGrowth,
    IReadOnlyList<PlanCountRow> PlanBreakdown,
    IReadOnlyList<TrialExpiringRow> ExpiringTrials);

public sealed record MonthlyGrowthRow(string Month, int NewClinics);
public sealed record PlanCountRow(string PlanName, int Count);

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
