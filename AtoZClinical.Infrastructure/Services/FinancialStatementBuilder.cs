using AtoZClinical.Core.Entities;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>Builds balance sheet and cash positions from trial balance journal totals.</summary>
public static class FinancialStatementBuilder
{
    public static readonly string[] LiquidAssetAccountNames = ["Cash", "Bank", "Visa Card"];

    public static IReadOnlyList<string> ResolveLiquidAccounts(
        string? paymentMethod,
        IReadOnlyList<ChartAccount> chartAccounts)
    {
        if (!string.IsNullOrWhiteSpace(paymentMethod) &&
            !string.Equals(paymentMethod, "All", StringComparison.OrdinalIgnoreCase))
        {
            return [PaymentJournalHelper.ResolvePaymentCreditAccount(paymentMethod, chartAccounts)];
        }

        var names = new List<string>();
        foreach (var preferred in LiquidAssetAccountNames)
        {
            var match = chartAccounts.FirstOrDefault(a =>
                string.Equals(a.Name, preferred, StringComparison.OrdinalIgnoreCase));
            names.Add(match?.Name ?? preferred);
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static decimal SumLiquidBalance(
        IReadOnlyList<JournalReportService.TrialBalanceRow> trialBalance,
        IReadOnlyList<string> accountNames) =>
        trialBalance
            .Where(r => accountNames.Contains(r.AccountName, StringComparer.OrdinalIgnoreCase))
            .Sum(r => r.Balance);

    public static decimal ComputeUnclosedNetIncome(IEnumerable<JournalReportService.TrialBalanceRow> trialBalance)
    {
        decimal net = 0;
        foreach (var row in trialBalance)
        {
            if (row.AccountCategory.Equals("Income", StringComparison.OrdinalIgnoreCase))
                net += -row.Balance;
            else if (row.AccountCategory.Equals("Expense", StringComparison.OrdinalIgnoreCase))
                net -= row.Balance;
        }

        return net;
    }

    public static BalanceSheetSnapshot BuildBalanceSheet(
        IReadOnlyList<JournalReportService.TrialBalanceRow> trialBalance,
        decimal supplementalPatientCredit = 0)
    {
        var assets = new List<BalanceSheetLine>();
        var liabilities = new List<BalanceSheetLine>();
        var equity = new List<BalanceSheetLine>();

        foreach (var row in trialBalance.OrderBy(r => r.AccountNo).ThenBy(r => r.AccountName))
        {
            if (row.Balance == 0)
                continue;

            switch (row.AccountCategory.ToLowerInvariant())
            {
                case "asset":
                    assets.Add(new BalanceSheetLine(row.AccountName, row.Balance));
                    break;
                case "liability":
                    liabilities.Add(new BalanceSheetLine(row.AccountName, -row.Balance));
                    break;
                case "equity":
                    equity.Add(new BalanceSheetLine(row.AccountName, -row.Balance));
                    break;
            }
        }

        var netIncome = ComputeUnclosedNetIncome(trialBalance);
        if (netIncome != 0)
            equity.Add(new BalanceSheetLine("Net Income (unclosed)", netIncome));

        if (supplementalPatientCredit > 0)
            liabilities.Add(new BalanceSheetLine("Patient Credit (overpayments)", supplementalPatientCredit));

        return new BalanceSheetSnapshot(assets, liabilities, equity);
    }

    public sealed record BalanceSheetLine(string Account, decimal Amount);

    public sealed record BalanceSheetSnapshot(
        IReadOnlyList<BalanceSheetLine> Assets,
        IReadOnlyList<BalanceSheetLine> Liabilities,
        IReadOnlyList<BalanceSheetLine> Equity)
    {
        public decimal TotalAssets => Assets.Sum(r => r.Amount);
        public decimal TotalLiabilities => Liabilities.Sum(r => r.Amount);
        public decimal TotalEquity => Equity.Sum(r => r.Amount);
    }
}
