using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Billing;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public enum ClinicBlockReason
{
    None = 0,
    NoClinic = 1,
    Pending = 2,
    Suspended = 3,
    Expired = 4,
    PaymentRequired = 5
}

public sealed record ClinicAccessResult(bool IsAllowed, string Message, ClinicBlockReason Reason, Clinic? Clinic)
{
    public static ClinicAccessResult Allowed(Clinic clinic) =>
        new(true, string.Empty, ClinicBlockReason.None, clinic);

    public static ClinicAccessResult Blocked(string message, ClinicBlockReason reason, Clinic? clinic = null) =>
        new(false, message, reason, clinic);
}

public sealed class ClinicAccessService
{
    private readonly ClinicalDbContext _db;

    public ClinicAccessService(ClinicalDbContext db) => _db = db;

    public ClinicAccessResult Evaluate(Clinic? clinic)
    {
        if (clinic is null)
            return ClinicAccessResult.Blocked("No clinic is assigned to this account.", ClinicBlockReason.NoClinic);

        if (clinic.Status == ClinicStatus.Pending)
            return ClinicAccessResult.Blocked(
                "Your clinic registration is pending approval. You will be able to login once the vendor activates your account.",
                ClinicBlockReason.Pending, clinic);

        if (clinic.Status == ClinicStatus.Suspended)
            return ClinicAccessResult.Blocked(
                "Your clinic account is suspended. Please contact your system vendor.",
                ClinicBlockReason.Suspended, clinic);

        if (clinic.Status == ClinicStatus.Expired)
            return ClinicAccessResult.Blocked(
                "Your subscription license has expired. Please contact your vendor to renew.",
                ClinicBlockReason.Expired, clinic);

        if (clinic.LicenseExpires.HasValue && clinic.LicenseExpires.Value.Date < DateTime.UtcNow.Date)
        {
            var expiry = clinic.SubscriptionExpiryDate ?? clinic.LicenseExpires;
            if (expiry.HasValue && expiry.Value.Date < DateTime.UtcNow.Date)
            {
                var hasActivePaidStripe =
                    string.Equals(clinic.SubscriptionStatus, SubscriptionStatuses.Active, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(clinic.StripeSubscriptionId);

                if (!hasActivePaidStripe)
                    return ClinicAccessResult.Blocked(
                        $"Your subscription expired on {expiry.Value:d}. Please renew your plan to continue.",
                        ClinicBlockReason.Expired, clinic);
            }
        }

        if (SubscriptionStatuses.BlocksAccess(clinic.SubscriptionStatus))
            return ClinicAccessResult.Blocked(
                "Your subscription payment failed or is past due. Please update billing to restore access.",
                ClinicBlockReason.PaymentRequired, clinic);

        return ClinicAccessResult.Allowed(clinic);
    }

    public async Task<int> ExpireOverdueLicensesAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;
        var overdue = await _db.Clinics
            .Where(c => c.Status == ClinicStatus.Active || c.Status == ClinicStatus.Pending)
            .Where(c => c.LicenseExpires != null && c.LicenseExpires.Value.Date < today)
            .Where(c => !SubscriptionStatuses.IsPaid(c.SubscriptionStatus))
            .ToListAsync(cancellationToken);

        foreach (var clinic in overdue)
            clinic.Status = ClinicStatus.Expired;

        if (overdue.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return overdue.Count;
    }
}
