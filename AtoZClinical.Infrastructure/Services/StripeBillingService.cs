using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Billing;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;
using StripeSubscription = Stripe.Subscription;
using ClinicSubscriptionStatuses = AtoZClinical.Infrastructure.Billing.SubscriptionStatuses;

namespace AtoZClinical.Infrastructure.Services;

public interface IStripeBillingService
{
    bool IsConfigured { get; }
    Task<string?> CreateCheckoutSessionAsync(Guid clinicId, string planKey, string successUrl, string cancelUrl, CancellationToken ct = default);
    Task<string?> CreateCustomerPortalSessionAsync(Guid clinicId, string returnUrl, CancellationToken ct = default);
    Task HandleWebhookAsync(string json, string stripeSignature, CancellationToken ct = default);
}

public sealed class StripeBillingService : IStripeBillingService
{
    private readonly ClinicalDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<StripeBillingService> _logger;
    private readonly ClinicRuntimeCache _cache;

    public StripeBillingService(
        ClinicalDbContext db,
        IConfiguration config,
        ILogger<StripeBillingService> logger,
        ClinicRuntimeCache cache)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _cache = cache;
    }

    public bool IsConfigured =>
        _config.GetValue("Billing:Enabled", false) &&
        !string.IsNullOrWhiteSpace(_config["Stripe:SecretKey"]);

    public async Task<string?> CreateCheckoutSessionAsync(
        Guid clinicId,
        string planKey,
        string successUrl,
        string cancelUrl,
        CancellationToken ct = default)
    {
        EnsureConfigured();
        var plan = BillingPlanCatalog.FindByKey(planKey)
            ?? throw new InvalidOperationException("Unknown subscription plan.");

        var priceId = _config[plan.ConfigPriceKey]?.Trim();
        if (string.IsNullOrWhiteSpace(priceId))
            throw new InvalidOperationException($"Stripe price is not configured for plan '{plan.DisplayName}'.");

        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct)
            ?? throw new InvalidOperationException("Clinic not found.");

        var customerId = await EnsureCustomerAsync(clinic, ct);

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            Customer = customerId,
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            ClientReferenceId = clinic.Id.ToString(),
            Metadata = new Dictionary<string, string>
            {
                ["clinicId"] = clinic.Id.ToString(),
                ["planKey"] = plan.Key
            },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = priceId,
                    Quantity = 1
                }
            ],
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    ["clinicId"] = clinic.Id.ToString(),
                    ["planKey"] = plan.Key
                }
            }
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options, cancellationToken: ct);
        return session.Url;
    }

    public async Task<string?> CreateCustomerPortalSessionAsync(Guid clinicId, string returnUrl, CancellationToken ct = default)
    {
        EnsureConfigured();
        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct)
            ?? throw new InvalidOperationException("Clinic not found.");

        if (string.IsNullOrWhiteSpace(clinic.StripeCustomerId))
            throw new InvalidOperationException("No billing account exists for this clinic yet.");

        var options = new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = clinic.StripeCustomerId,
            ReturnUrl = returnUrl
        };

        var service = new Stripe.BillingPortal.SessionService();
        var session = await service.CreateAsync(options, cancellationToken: ct);
        return session.Url;
    }

    public async Task HandleWebhookAsync(string json, string stripeSignature, CancellationToken ct = default)
    {
        EnsureConfigured();
        var webhookSecret = _config["Stripe:WebhookSecret"]?.Trim()
            ?? throw new InvalidOperationException("Stripe webhook secret is not configured.");

        var stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, webhookSecret);

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await OnCheckoutCompletedAsync(stripeEvent, ct);
                break;
            case "customer.subscription.updated":
            case "customer.subscription.created":
                await OnSubscriptionUpdatedAsync(stripeEvent, ct);
                break;
            case "customer.subscription.deleted":
                await OnSubscriptionDeletedAsync(stripeEvent, ct);
                break;
            case "invoice.payment_failed":
                await OnPaymentFailedAsync(stripeEvent, ct);
                break;
        }
    }

    private async Task OnCheckoutCompletedAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Session session)
            return;

        if (!Guid.TryParse(session.Metadata.GetValueOrDefault("clinicId"), out var clinicId))
            clinicId = Guid.TryParse(session.ClientReferenceId, out var parsed) ? parsed : Guid.Empty;

        if (clinicId == Guid.Empty)
            return;

        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null) return;

        clinic.StripeCustomerId ??= session.CustomerId;
        clinic.StripeSubscriptionId = session.SubscriptionId;
        clinic.SubscriptionStatus = ClinicSubscriptionStatuses.Active;
        clinic.Status = ClinicStatus.Active;

        var planKey = session.Metadata.GetValueOrDefault("planKey");
        ApplyPlan(clinic, planKey);
        ExtendLicense(clinic);

        await _db.SaveChangesAsync(ct);
        _cache.InvalidateClinic(clinicId);
        _logger.LogInformation("Checkout completed for clinic {ClinicId}", clinicId);
    }

    private async Task OnSubscriptionUpdatedAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not StripeSubscription subscription)
            return;

        var clinic = await FindClinicBySubscriptionAsync(subscription, ct);
        if (clinic is null) return;

        clinic.StripeSubscriptionId = subscription.Id;
        clinic.StripeCustomerId ??= subscription.CustomerId;
        clinic.SubscriptionStatus = subscription.Status ?? ClinicSubscriptionStatuses.None;

        var planKey = subscription.Metadata.GetValueOrDefault("planKey");
        ApplyPlan(clinic, planKey);

        if (ClinicSubscriptionStatuses.IsPaid(clinic.SubscriptionStatus))
        {
            clinic.Status = ClinicStatus.Active;
            ExtendLicense(clinic);
        }
        else if (ClinicSubscriptionStatuses.BlocksAccess(clinic.SubscriptionStatus))
        {
            clinic.Status = ClinicStatus.Suspended;
        }

        await _db.SaveChangesAsync(ct);
        _cache.InvalidateClinic(clinic.Id);
    }

    private async Task OnSubscriptionDeletedAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not StripeSubscription subscription)
            return;

        var clinic = await FindClinicBySubscriptionAsync(subscription, ct);
        if (clinic is null) return;

        clinic.SubscriptionStatus = ClinicSubscriptionStatuses.Canceled;
        clinic.StripeSubscriptionId = null;
        if (clinic.LicenseExpires is null || clinic.LicenseExpires.Value.Date < DateTime.UtcNow.Date)
            clinic.Status = ClinicStatus.Expired;

        await _db.SaveChangesAsync(ct);
        _cache.InvalidateClinic(clinic.Id);
    }

    private async Task OnPaymentFailedAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Stripe.Invoice invoice || string.IsNullOrWhiteSpace(invoice.SubscriptionId))
            return;

        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.StripeSubscriptionId == invoice.SubscriptionId, ct);
        if (clinic is null) return;

        clinic.SubscriptionStatus = ClinicSubscriptionStatuses.PastDue;
        await _db.SaveChangesAsync(ct);
        _cache.InvalidateClinic(clinic.Id);
    }

    private async Task<Clinic?> FindClinicBySubscriptionAsync(StripeSubscription subscription, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(subscription.Id))
        {
            var bySub = await _db.Clinics.FirstOrDefaultAsync(c => c.StripeSubscriptionId == subscription.Id, ct);
            if (bySub is not null) return bySub;
        }

        if (subscription.Metadata.TryGetValue("clinicId", out var clinicIdText) &&
            Guid.TryParse(clinicIdText, out var clinicId))
            return await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct);

        if (!string.IsNullOrWhiteSpace(subscription.CustomerId))
            return await _db.Clinics.FirstOrDefaultAsync(c => c.StripeCustomerId == subscription.CustomerId, ct);

        return null;
    }

    private async Task<string> EnsureCustomerAsync(Clinic clinic, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(clinic.StripeCustomerId))
            return clinic.StripeCustomerId;

        var service = new CustomerService();
        var customer = await service.CreateAsync(new CustomerCreateOptions
        {
            Email = clinic.Email,
            Name = clinic.Name,
            Metadata = new Dictionary<string, string> { ["clinicId"] = clinic.Id.ToString() }
        }, cancellationToken: ct);

        clinic.StripeCustomerId = customer.Id;
        await _db.SaveChangesAsync(ct);
        return customer.Id;
    }

    private static void ApplyPlan(Clinic clinic, string? planKey)
    {
        var plan = BillingPlanCatalog.FindByKey(planKey) ?? BillingPlanCatalog.FindByPlanName(clinic.PlanName);
        if (plan is null) return;

        clinic.PlanName = plan.DisplayName;
        clinic.MaxUsers = plan.MaxUsers;
    }

    private static void ExtendLicense(Clinic clinic)
    {
        var renewUntil = DateTime.UtcNow.Date.AddMonths(1);
        if (!clinic.LicenseExpires.HasValue || clinic.LicenseExpires.Value.Date < renewUntil)
            clinic.LicenseExpires = renewUntil;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Stripe billing is not configured.");

        StripeConfiguration.ApiKey = _config["Stripe:SecretKey"]!.Trim();
    }
}

public sealed class NoOpStripeBillingService : IStripeBillingService
{
    public bool IsConfigured => false;

    public Task<string?> CreateCheckoutSessionAsync(Guid clinicId, string planKey, string successUrl, string cancelUrl, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> CreateCustomerPortalSessionAsync(Guid clinicId, string returnUrl, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task HandleWebhookAsync(string json, string stripeSignature, CancellationToken ct = default) =>
        Task.CompletedTask;
}
