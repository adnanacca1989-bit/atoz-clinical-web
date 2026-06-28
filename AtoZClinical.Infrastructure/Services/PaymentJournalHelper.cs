using AtoZClinical.Core.Entities;

namespace AtoZClinical.Infrastructure.Services;

public static class PaymentJournalHelper
{
    public const string CashPaymentSource = "CashPayment";

    public static string ResolvePaymentCreditAccount(
        string paymentMethod,
        IReadOnlyList<ChartAccount> accounts,
        string? overrideAccountName = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideAccountName))
        {
            var match = accounts.FirstOrDefault(a =>
                string.Equals(a.Name, overrideAccountName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.Name;
        }

        return MapPaymentMethod(paymentMethod) switch
        {
            "Bank" => FindAccount(accounts, "Asset", "Bank") ?? "Bank",
            "Credit" => FindAccount(accounts, "Liability", "Account Payable") ?? "Account Payable",
            _ => FindAccount(accounts, "Asset", "Cash") ?? "Cash"
        };
    }

    public static string MapPaymentMethod(string paymentMethod)
    {
        var m = paymentMethod.Trim();
        if (m.Equals("Bank Transfer", StringComparison.OrdinalIgnoreCase) ||
            m.Equals("Bank", StringComparison.OrdinalIgnoreCase))
            return "Bank";
        if (m.Equals("Credit", StringComparison.OrdinalIgnoreCase) ||
            m.Equals("On Account", StringComparison.OrdinalIgnoreCase))
            return "Credit";
        return "Cash";
    }

    public static bool IsVendorCreditPayment(string paymentMethod) =>
        MapPaymentMethod(paymentMethod) == "Credit";

    private static string? FindAccount(IReadOnlyList<ChartAccount> accounts, string category, string detailOrName)
    {
        var match = accounts.FirstOrDefault(a =>
            string.Equals(a.CategoryType, category, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(a.DetailType, detailOrName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(a.Name, detailOrName, StringComparison.OrdinalIgnoreCase)));
        return match?.Name;
    }
}
