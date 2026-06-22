namespace AtoZClinical.Infrastructure.Services;

public static class AmountWords
{
    private static readonly string[] Ones =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
        "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"
    ];

    private static readonly string[] Tens =
        ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    public static string Convert(decimal amount)
    {
        if (amount == 0) return "Zero";

        var whole = (long)Math.Truncate(Math.Abs(amount));
        var cents = (int)Math.Round((Math.Abs(amount) - whole) * 100, MidpointRounding.AwayFromZero);
        var words = ConvertWhole(whole);
        if (amount < 0) words = "Negative " + words;
        if (cents > 0) words += $" and {cents:00}/100";
        return words;
    }

    private static string ConvertWhole(long n)
    {
        if (n < 20) return Ones[n];
        if (n < 100) return Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "");
        if (n < 1000) return Ones[n / 100] + " Hundred" + (n % 100 > 0 ? " " + ConvertWhole(n % 100) : "");
        if (n < 1_000_000) return ConvertWhole(n / 1000) + " Thousand" + (n % 1000 > 0 ? " " + ConvertWhole(n % 1000) : "");
        if (n < 1_000_000_000) return ConvertWhole(n / 1_000_000) + " Million" + (n % 1_000_000 > 0 ? " " + ConvertWhole(n % 1_000_000) : "");
        return ConvertWhole(n / 1_000_000_000) + " Billion" + (n % 1_000_000_000 > 0 ? " " + ConvertWhole(n % 1_000_000_000) : "");
    }
}
