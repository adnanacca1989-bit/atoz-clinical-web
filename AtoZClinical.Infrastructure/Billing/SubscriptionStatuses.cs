namespace AtoZClinical.Infrastructure.Billing;

public static class SubscriptionStatuses
{
    public const string None = "none";
    public const string Trialing = "trialing";
    public const string Active = "active";
    public const string PastDue = "past_due";
    public const string Canceled = "canceled";
    public const string Unpaid = "unpaid";

    public static bool IsPaid(string? status) =>
        string.Equals(status, Active, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Trialing, StringComparison.OrdinalIgnoreCase);

    public static bool BlocksAccess(string? status) =>
        string.Equals(status, PastDue, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Unpaid, StringComparison.OrdinalIgnoreCase);
}

public static class SubscriptionPlans
{
    public const string Trial = "Trial";
    public const string Standard = "Standard";
    public const string Professional = "Professional";
}

public sealed record BillingPlan(
    string Key,
    string DisplayName,
    decimal MonthlyPriceUsd,
    int MaxUsers,
    string ConfigPriceKey);

public static class BillingPlanCatalog
{
    public static readonly BillingPlan Standard = new(
        "standard",
        SubscriptionPlans.Standard,
        99m,
        25,
        "Stripe:PriceIdStandard");

    public static readonly BillingPlan Professional = new(
        "professional",
        SubscriptionPlans.Professional,
        199m,
        100,
        "Stripe:PriceIdProfessional");

    public static IReadOnlyList<BillingPlan> PaidPlans { get; } = [Standard, Professional];

    public static BillingPlan? FindByKey(string? key) =>
        PaidPlans.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static BillingPlan? FindByPlanName(string? planName) =>
        PaidPlans.FirstOrDefault(p => p.DisplayName.Equals(planName, StringComparison.OrdinalIgnoreCase));
}
