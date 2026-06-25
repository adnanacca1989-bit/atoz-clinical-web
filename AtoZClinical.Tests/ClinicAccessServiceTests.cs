using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Billing;
using AtoZClinical.Infrastructure.Services;

namespace AtoZClinical.Tests;

public class ClinicAccessServiceTests
{
    private readonly ClinicAccessService _service = new(null!);

    [Fact]
    public void Evaluate_allows_active_clinic_with_valid_license()
    {
        var clinic = new Clinic
        {
            Status = ClinicStatus.Active,
            LicenseExpires = DateTime.UtcNow.Date.AddMonths(1),
            SubscriptionStatus = SubscriptionStatuses.Active
        };

        var result = _service.Evaluate(clinic);
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_blocks_past_due_subscription()
    {
        var clinic = new Clinic
        {
            Status = ClinicStatus.Active,
            LicenseExpires = DateTime.UtcNow.Date.AddMonths(1),
            SubscriptionStatus = SubscriptionStatuses.PastDue
        };

        var result = _service.Evaluate(clinic);
        Assert.False(result.IsAllowed);
        Assert.Equal(ClinicBlockReason.PaymentRequired, result.Reason);
    }

    [Fact]
    public void Evaluate_blocks_expired_trial_without_paid_subscription()
    {
        var clinic = new Clinic
        {
            Status = ClinicStatus.Active,
            PlanName = SubscriptionPlans.Trial,
            LicenseExpires = DateTime.UtcNow.Date.AddDays(-1),
            SubscriptionStatus = SubscriptionStatuses.Trialing
        };

        var result = _service.Evaluate(clinic);
        Assert.False(result.IsAllowed);
        Assert.Equal(ClinicBlockReason.Expired, result.Reason);
    }

    [Fact]
    public void Evaluate_allows_paid_subscription_past_license_date()
    {
        var clinic = new Clinic
        {
            Status = ClinicStatus.Active,
            LicenseExpires = DateTime.UtcNow.Date.AddDays(-1),
            SubscriptionStatus = SubscriptionStatuses.Active,
            StripeSubscriptionId = "sub_123"
        };

        var result = _service.Evaluate(clinic);
        Assert.True(result.IsAllowed);
    }
}
