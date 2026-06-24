using AtoZClinical.Core.Entities;

namespace AtoZClinical.Infrastructure.Services;

public static class InvoiceArCalculator
{
    public sealed record ArTotals(
        decimal CashReceipt,
        decimal CashPayment,
        decimal TotalInvoice,
        decimal Discount,
        decimal NetInvoice,
        decimal EndingBalance,
        decimal AmountApplied);

    public static ArTotals ForInvoice(
        Invoice invoice,
        IReadOnlyList<CashReceipt> receipts,
        IReadOnlyList<CashPayment> payments,
        IReadOnlyList<Invoice> siblingInvoices)
    {
        var matchingReceipts = receipts.Where(r => MatchesReceipt(r, invoice)).ToList();
        var matchingPayments = payments.Where(p => MatchesPayment(p, invoice)).ToList();
        var receiptTotal = matchingReceipts.Sum(r => r.Amount);
        var paymentTotal = matchingPayments.Sum(p => p.Amount);

        var siblings = siblingInvoices
            .Where(i => SamePatientDoctor(i, invoice))
            .OrderBy(i => i.InvoiceDate)
            .ThenBy(i => i.InvoiceNo)
            .ToList();

        if (siblings.Count > 1)
        {
            var allocated = AllocateDisplayAmounts(siblings, matchingReceipts, matchingPayments);
            if (allocated.TryGetValue(invoice.InvoiceNo, out var split))
            {
                receiptTotal = split.CashReceipt;
                paymentTotal = split.CashPayment;
            }
        }

        var totalInvoice = invoice.SubTotal > 0 ? invoice.SubTotal : invoice.TotalAmount + invoice.Discount;
        var netInvoice = invoice.TotalAmount;
        var amountApplied = invoice.AmountPaid;
        var endingBalance = invoice.BalanceDue;

        return new ArTotals(
            receiptTotal,
            paymentTotal,
            totalInvoice,
            invoice.Discount,
            netInvoice,
            endingBalance,
            amountApplied);
    }

    public static Dictionary<int, (decimal CashReceipt, decimal CashPayment)> AllocateDisplayAmounts(
        IReadOnlyList<Invoice> invoices,
        IReadOnlyList<CashReceipt> receipts,
        IReadOnlyList<CashPayment> payments)
    {
        var result = invoices.ToDictionary(i => i.InvoiceNo, _ => (CashReceipt: 0m, CashPayment: 0m));
        if (invoices.Count == 0) return result;

        var remainingDue = invoices.ToDictionary(i => i.InvoiceNo, i => i.TotalAmount);
        var credits = receipts
            .Select(r => new { IsReceipt = true, Date = r.ReceiptDate, Amount = r.Amount, Sort = r.ReceiptNo })
            .Concat(payments.Select(p => new { IsReceipt = false, Date = p.PaymentDate, Amount = p.Amount, Sort = p.PaymentNo }))
            .OrderBy(c => c.Date)
            .ThenBy(c => c.Sort)
            .ToList();

        foreach (var credit in credits)
        {
            var remaining = credit.Amount;
            foreach (var inv in invoices.OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNo))
            {
                if (remaining <= 0) break;
                var due = remainingDue[inv.InvoiceNo];
                if (due <= 0) continue;

                var apply = Math.Min(remaining, due);
                var current = result[inv.InvoiceNo];
                result[inv.InvoiceNo] = credit.IsReceipt
                    ? (current.CashReceipt + apply, current.CashPayment)
                    : (current.CashReceipt, current.CashPayment + apply);
                remainingDue[inv.InvoiceNo] -= apply;
                remaining -= apply;
            }
        }

        return result;
    }

    public static bool MatchesReceipt(CashReceipt receipt, Invoice invoice) =>
        MatchesPatient(receipt.PatientId, receipt.PatientName, invoice) &&
        MatchesDoctor(receipt.DoctorName, invoice.DoctorName);

    public static bool MatchesPayment(CashPayment payment, Invoice invoice) =>
        MatchesPatient(payment.PatientId, payment.PayeeName, invoice) &&
        MatchesDoctor(payment.DoctorName, invoice.DoctorName);

    private static bool SamePatientDoctor(Invoice a, Invoice b) =>
        MatchesPatient(a.PatientId, a.PatientName, b) &&
        MatchesDoctor(a.DoctorName, b.DoctorName);

    private static bool MatchesPatient(string? transactionPatientId, string? transactionPatientName, Invoice invoice)
    {
        if (!string.IsNullOrWhiteSpace(invoice.PatientId) &&
            string.Equals(transactionPatientId?.Trim(), invoice.PatientId.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(invoice.PatientName) &&
               string.Equals(transactionPatientName?.Trim(), invoice.PatientName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDoctor(string? transactionDoctor, string? invoiceDoctor) =>
        string.IsNullOrWhiteSpace(invoiceDoctor) ||
        string.Equals(transactionDoctor?.Trim(), invoiceDoctor.Trim(), StringComparison.OrdinalIgnoreCase);
}
