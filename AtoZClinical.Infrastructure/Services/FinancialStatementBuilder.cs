using AtoZClinical.Core.Entities;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>Builds balance sheet and cash positions from trial balance journal totals.</summary>
public static class FinancialStatementBuilder
{
    public static readonly string[] LiquidAssetAccountNames = ["Cash", "Bank", "Visa Card"];
    public const string CashEquivalentsLabel = "Cash & Cash Equivalents";
    public const string AccountsReceivableName = "Accounts Receivable";
    public const string PatientDepositsLabel = "Patient Deposits (unapplied)";

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
        decimal supplementalOpenAr = 0)
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
                    if (IsAccountsReceivable(row.AccountName) && row.Balance < 0)
                    {
                        liabilities.Add(new BalanceSheetLine(PatientDepositsLabel, -row.Balance));
                        break;
                    }

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

        ApplySupplementalOpenAr(assets, trialBalance, supplementalOpenAr);

        var netIncome = ComputeUnclosedNetIncome(trialBalance);
        if (netIncome != 0)
            equity.Add(new BalanceSheetLine("Net Income (unclosed)", netIncome));

        assets = AggregateLiquidAssets(assets);

        return new BalanceSheetSnapshot(assets, liabilities, equity);
    }

    public static List<BalanceSheetLine> AggregateLiquidAssets(IReadOnlyList<BalanceSheetLine> assets)
    {
        var liquidSet = new HashSet<string>(LiquidAssetAccountNames, StringComparer.OrdinalIgnoreCase);
        var liquidLines = assets.Where(a => liquidSet.Contains(a.Account)).ToList();
        if (liquidLines.Count <= 1)
            return assets.ToList();

        var other = assets.Where(a => !liquidSet.Contains(a.Account)).ToList();
        other.Add(new BalanceSheetLine(CashEquivalentsLabel, liquidLines.Sum(l => l.Amount)));
        return other;
    }

    private static void ApplySupplementalOpenAr(
        List<BalanceSheetLine> assets,
        IReadOnlyList<JournalReportService.TrialBalanceRow> trialBalance,
        decimal supplementalOpenAr)
    {
        if (supplementalOpenAr <= 0)
            return;

        var journalAr = trialBalance
            .Where(r => IsAccountsReceivable(r.AccountName))
            .Sum(r => r.Balance);

        if (journalAr >= supplementalOpenAr)
            return;

        var gap = supplementalOpenAr - Math.Max(0m, journalAr);
        if (gap <= 0)
            return;

        var existing = assets.FindIndex(a => IsAccountsReceivable(a.Account));
        if (existing >= 0)
            assets[existing] = assets[existing] with { Amount = assets[existing].Amount + gap };
        else
            assets.Add(new BalanceSheetLine(AccountsReceivableName, supplementalOpenAr));
    }

    public static bool IsAccountsReceivable(string accountName) =>
        accountName.Contains("Receivable", StringComparison.OrdinalIgnoreCase);

    public sealed record BalanceSheetLine(string Account, decimal Amount);

    public sealed record BalanceSheetSnapshot(
        IReadOnlyList<BalanceSheetLine> Assets,
        IReadOnlyList<BalanceSheetLine> Liabilities,
        IReadOnlyList<BalanceSheetLine> Equity)
    {
        public decimal TotalAssets => Assets.Sum(r => r.Amount);
        public decimal TotalLiabilities => Liabilities.Sum(r => r.Amount);
        public decimal TotalEquity => Equity.Sum(r => r.Amount);
        public bool IsBalanced => Math.Abs(TotalAssets - (TotalLiabilities + TotalEquity)) < 0.01m;
    }
}
