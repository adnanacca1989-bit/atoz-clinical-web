namespace AtoZClinical.Infrastructure.Services;

public static class ArBalanceFormatter
{
    /// <summary>Positive = debit (patient owes). Negative = credit (overpaid).</summary>
    public static string Format(decimal balance) =>
        balance < 0 ? $"{Math.Abs(balance):N2} Cr" : balance > 0 ? $"{balance:N2} Dr" : "0.00";

    public static string FormatSigned(decimal balance) =>
        balance.ToString("N2");
}
