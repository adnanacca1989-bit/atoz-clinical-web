using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Billing;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class SaasSubscriptionService
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicRuntimeCache _cache;

    public SaasSubscriptionService(ClinicalDbContext db, ClinicRuntimeCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public static void ApplyPlanDefaults(Clinic clinic, string? planName)
    {
        var plan = BillingPlanCatalog.FindByPlanName(planName)
            ?? BillingPlanCatalog.FindByKey(planName);

        if (planName?.Equals(SubscriptionPlans.Trial, StringComparison.OrdinalIgnoreCase) == true)
        {
            clinic.PlanName = SubscriptionPlans.Trial;
            clinic.SubscriptionType = SubscriptionPlans.Trial;
            clinic.MaxUsers = Math.Max(clinic.MaxUsers, 10);
            clinic.SubscriptionStatus = SubscriptionStatuses.Trialing;
            return;
        }

        if (plan is not null)
        {
            clinic.PlanName = plan.DisplayName;
            clinic.SubscriptionType = plan.DisplayName;
            clinic.MaxUsers = plan.MaxUsers;
        }
        else if (!string.IsNullOrWhiteSpace(planName))
        {
            clinic.PlanName = planName.Trim();
            clinic.SubscriptionType = planName.Trim();
        }
    }

    public static void SyncSubscriptionDates(Clinic clinic, DateTime? start = null, DateTime? expiry = null)
    {
        var startDate = (start ?? clinic.SubscriptionStartDate ?? clinic.CreatedAt).Date;
        clinic.SubscriptionStartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);

        var expiryDate = (expiry
            ?? clinic.SubscriptionExpiryDate
            ?? clinic.LicenseExpires
            ?? clinic.TrialEndsAt
            ?? DateTime.UtcNow.Date.AddYears(1)).Date;
        clinic.SubscriptionExpiryDate = DateTime.SpecifyKind(expiryDate, DateTimeKind.Utc);
        clinic.LicenseExpires = clinic.SubscriptionExpiryDate;

        if (clinic.PlanName.Equals(SubscriptionPlans.Trial, StringComparison.OrdinalIgnoreCase))
            clinic.TrialEndsAt = clinic.SubscriptionExpiryDate;
    }

    public async Task RenewSubscriptionAsync(
        Guid clinicId,
        DateTime expiryDate,
        string? planName = null,
        int? maxUsers = null,
        CancellationToken ct = default)
    {
        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct)
            ?? throw new InvalidOperationException("Clinic not found.");

        if (!string.IsNullOrWhiteSpace(planName))
            ApplyPlanDefaults(clinic, planName);

        if (maxUsers is > 0)
            clinic.MaxUsers = maxUsers.Value;

        clinic.Status = ClinicStatus.Active;
        clinic.SubscriptionStatus = clinic.PlanName.Equals(SubscriptionPlans.Trial, StringComparison.OrdinalIgnoreCase)
            ? SubscriptionStatuses.Trialing
            : SubscriptionStatuses.Active;

        SyncSubscriptionDates(clinic, DateTime.UtcNow.Date, expiryDate);
        await _db.SaveChangesAsync(ct);
        _cache.InvalidateClinic(clinicId);
    }

    public async Task<int> ExpireOverdueSubscriptionsAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var overdue = await _db.Clinics
            .Where(c => c.Status == ClinicStatus.Active || c.Status == ClinicStatus.Pending)
            .Where(c =>
                (c.SubscriptionExpiryDate != null && c.SubscriptionExpiryDate.Value.Date < today) ||
                (c.SubscriptionExpiryDate == null && c.LicenseExpires != null && c.LicenseExpires.Value.Date < today))
            .Where(c => !SubscriptionStatuses.IsPaid(c.SubscriptionStatus) ||
                        c.PlanName.Equals(SubscriptionPlans.Trial, StringComparison.OrdinalIgnoreCase))
            .ToListAsync(ct);

        foreach (var clinic in overdue)
            clinic.Status = ClinicStatus.Expired;

        if (overdue.Count > 0)
            await _db.SaveChangesAsync(ct);

        return overdue.Count;
    }

    public async Task<IReadOnlyList<SubscriptionReportRow>> GetSubscriptionReportAsync(CancellationToken ct = default)
    {
        var clinics = await _db.Clinics.AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        return clinics.Select(c => new SubscriptionReportRow(
            c.Id,
            c.ClinicCode,
            c.Name,
            c.SubscriptionType,
            c.PlanName,
            c.SubscriptionStatus,
            c.Status,
            c.SubscriptionStartDate,
            c.SubscriptionExpiryDate ?? c.LicenseExpires,
            c.MaxUsers)).ToList();
    }
}

public sealed record SubscriptionReportRow(
    Guid ClinicId,
    string ClinicCode,
    string Name,
    string SubscriptionType,
    string PlanName,
    string SubscriptionStatus,
    ClinicStatus Status,
    DateTime? StartDate,
    DateTime? ExpiryDate,
    int MaxUsers);
