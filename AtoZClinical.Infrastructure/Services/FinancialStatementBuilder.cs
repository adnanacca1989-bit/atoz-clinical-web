using AtoZClinical.Core.Entities;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>Builds P&amp;L and balance sheet from posted journal trial balance / period activity.</summary>
public static class FinancialStatementBuilder
{
    public static readonly string[] LiquidAssetAccountNames = ["Cash", "Bank", "Visa Card"];
    public const string CashEquivalentsLabel = "Cash & Cash Equivalents";
    public const string AccountsReceivableName = "Accounts Receivable";
    public const string PatientDepositsLabel = "Patient Deposits (unapplied)";
    public const string NetIncomeLabel = "Net Income (unclosed)";

    private static readonly HashSet<string> CurrentAssetDetailTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cash", "Bank", "Visa Card", "Accounts Receivable", "Inventory", "Pre-Paid Expenses", "Health Insurance"
    };

    private static readonly HashSet<string> CurrentLiabilityDetailTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Account Payable", "Accrual Expenses"
    };

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

    public static ProfitAndLossSnapshot BuildProfitAndLoss(
        IReadOnlyList<JournalReportService.PeriodActivityRow> periodActivity,
        bool nonZeroOnly = false)
    {
        var income = new List<StatementLine>();
        var cogs = new List<StatementLine>();
        var expenses = new List<StatementLine>();

        foreach (var row in periodActivity.OrderBy(r => r.AccountNo).ThenBy(r => r.AccountName))
        {
            if (row.AccountCategory.Equals("Income", StringComparison.OrdinalIgnoreCase))
            {
                var amount = row.IncomeAmount;
                if (nonZeroOnly && amount == 0) continue;
                income.Add(new StatementLine(row.AccountName, amount, row.DetailType));
            }
            else if (row.AccountCategory.Equals("Expense", StringComparison.OrdinalIgnoreCase))
            {
                var amount = row.ExpenseAmount;
                if (nonZeroOnly && amount == 0) continue;
                var line = new StatementLine(row.AccountName, amount, row.DetailType);
                if (IsCostOfGoodsSold(row.DetailType, row.AccountName))
                    cogs.Add(line);
                else
                    expenses.Add(line);
            }
        }

        var totalIncome = income.Sum(l => l.Amount);
        var totalCogs = cogs.Sum(l => l.Amount);
        var totalExpenses = expenses.Sum(l => l.Amount);
        var grossProfit = totalIncome - totalCogs;
        var netIncome = grossProfit - totalExpenses;

        return new ProfitAndLossSnapshot(income, cogs, expenses, totalIncome, totalCogs, totalExpenses, grossProfit, netIncome);
    }

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
        IReadOnlyList<ChartAccount> chartAccounts)
    {
        var detailByName = chartAccounts.ToDictionary(a => a.Name, a => a.DetailType, StringComparer.OrdinalIgnoreCase);

        var currentAssets = new List<BalanceSheetLine>();
        var nonCurrentAssets = new List<BalanceSheetLine>();
        var currentLiabilities = new List<BalanceSheetLine>();
        var nonCurrentLiabilities = new List<BalanceSheetLine>();
        var equity = new List<BalanceSheetLine>();

        foreach (var row in trialBalance.OrderBy(r => r.AccountNo).ThenBy(r => r.AccountName))
        {
            if (row.Balance == 0)
                continue;

            detailByName.TryGetValue(row.AccountName, out var detailType);
            detailType ??= row.AccountCategory;

            switch (row.AccountCategory.ToLowerInvariant())
            {
                case "asset":
                    if (IsAccountsReceivable(row.AccountName) && row.Balance < 0)
                    {
                        currentLiabilities.Add(new BalanceSheetLine(PatientDepositsLabel, -row.Balance, detailType));
                        break;
                    }

                    if (IsCurrentAsset(detailType, row.AccountName))
                        currentAssets.Add(new BalanceSheetLine(row.AccountName, row.Balance, detailType));
                    else
                        nonCurrentAssets.Add(new BalanceSheetLine(row.AccountName, row.Balance, detailType));
                    break;

                case "liability":
                    var liabilityAmount = -row.Balance;
                    if (IsCurrentLiability(detailType))
                        currentLiabilities.Add(new BalanceSheetLine(row.AccountName, liabilityAmount, detailType));
                    else
                        nonCurrentLiabilities.Add(new BalanceSheetLine(row.AccountName, liabilityAmount, detailType));
                    break;

                case "equity":
                    equity.Add(new BalanceSheetLine(row.AccountName, -row.Balance, detailType));
                    break;
            }
        }

        currentAssets = AggregateLiquidAssets(currentAssets);

        var netIncome = ComputeUnclosedNetIncome(trialBalance);
        if (netIncome != 0)
            equity.Add(new BalanceSheetLine(NetIncomeLabel, netIncome, "Retained Earnings"));

        var assetSections = BuildSections(
            ("Current Assets", currentAssets),
            ("Non-current Assets", nonCurrentAssets));

        var liabilitySections = BuildSections(
            ("Current Liabilities", currentLiabilities),
            ("Non-current Liabilities", nonCurrentLiabilities));

        var equitySections = BuildSections(
            ("Equity", equity));

        var flatAssets = assetSections.SelectMany(s => s.Lines).ToList();
        var flatLiabilities = liabilitySections.SelectMany(s => s.Lines).ToList();
        var flatEquity = equitySections.SelectMany(s => s.Lines).ToList();

        return new BalanceSheetSnapshot(assetSections, liabilitySections, equitySections, flatAssets, flatLiabilities, flatEquity);
    }

    private static IReadOnlyList<BalanceSheetSection> BuildSections(
        params (string Title, List<BalanceSheetLine> Lines)[] sections)
    {
        var result = new List<BalanceSheetSection>();
        foreach (var (title, lines) in sections)
        {
            if (lines.Count == 0) continue;
            result.Add(new BalanceSheetSection(title, lines));
        }

        return result;
    }

    public static List<BalanceSheetLine> AggregateLiquidAssets(IReadOnlyList<BalanceSheetLine> assets)
    {
        var liquidSet = new HashSet<string>(LiquidAssetAccountNames, StringComparer.OrdinalIgnoreCase);
        var liquidLines = assets.Where(a => liquidSet.Contains(a.Account)).ToList();
        if (liquidLines.Count <= 1)
            return assets.ToList();

        var other = assets.Where(a => !liquidSet.Contains(a.Account)).ToList();
        other.Insert(0, new BalanceSheetLine(CashEquivalentsLabel, liquidLines.Sum(l => l.Amount), "Cash"));
        return other;
    }

    public static bool IsCostOfGoodsSold(string? detailType, string accountName) =>
        detailType?.Contains("Cost of Goods", StringComparison.OrdinalIgnoreCase) == true
        || accountName.Contains("Cost of Goods", StringComparison.OrdinalIgnoreCase);

    public static bool IsAccountsReceivable(string accountName) =>
        accountName.Contains("Receivable", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentAsset(string? detailType, string accountName)
    {
        if (CurrentAssetDetailTypes.Contains(detailType ?? ""))
            return true;
        return LiquidAssetAccountNames.Contains(accountName, StringComparer.OrdinalIgnoreCase)
            || IsAccountsReceivable(accountName);
    }

    private static bool IsCurrentLiability(string? detailType) =>
        CurrentLiabilityDetailTypes.Contains(detailType ?? "")
        || string.Equals(detailType, PatientDepositsLabel, StringComparison.OrdinalIgnoreCase);

    public sealed record StatementLine(string Account, decimal Amount, string? DetailType = null);

    public sealed record ProfitAndLossSnapshot(
        IReadOnlyList<StatementLine> Income,
        IReadOnlyList<StatementLine> CostOfGoodsSold,
        IReadOnlyList<StatementLine> Expenses,
        decimal TotalIncome,
        decimal TotalCostOfGoodsSold,
        decimal TotalExpenses,
        decimal GrossProfit,
        decimal NetIncome);

    public sealed record BalanceSheetLine(string Account, decimal Amount, string? DetailType = null);

    public sealed record BalanceSheetSection(string Title, IReadOnlyList<BalanceSheetLine> Lines)
    {
        public decimal Subtotal => Lines.Sum(l => l.Amount);
    }

    public sealed record BalanceSheetSnapshot(
        IReadOnlyList<BalanceSheetSection> AssetSections,
        IReadOnlyList<BalanceSheetSection> LiabilitySections,
        IReadOnlyList<BalanceSheetSection> EquitySections,
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
