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
        decimal AmountApplied,
        decimal TotalReceived,
        decimal PatientCredit);

    public static ArTotals ForInvoice(
        Invoice invoice,
        IReadOnlyList<CashReceipt> receipts,
        IReadOnlyList<CashPayment> payments,
        IReadOnlyList<Invoice> siblingInvoices)
    {
        var matchingReceipts = ResolveMatchingReceipts(receipts, invoice);
        var matchingPayments = ResolveMatchingPayments(payments, invoice);

        var siblings = siblingInvoices
            .Where(i => SamePatientDoctor(i, invoice))
            .OrderBy(i => i.InvoiceDate)
            .ThenBy(i => i.InvoiceNo)
            .ToList();

        var allocated = AllocateDisplayAmounts(
            siblings.Count > 0 ? siblings : [invoice],
            matchingReceipts,
            matchingPayments);

        var split = allocated.TryGetValue(invoice.InvoiceNo, out var amounts)
            ? amounts
            : (CashReceipt: 0m, CashPayment: 0m);

        var receiptTotal = split.CashReceipt;
        var paymentTotal = split.CashPayment;
        var totalReceived = matchingReceipts.Sum(r => r.Amount) + matchingPayments.Sum(p => p.Amount);

        var totalInvoice = invoice.SubTotal > 0 ? invoice.SubTotal : invoice.TotalAmount + invoice.Discount;
        var netInvoice = invoice.TotalAmount;
        var amountApplied = invoice.AmountPaid;

        // Positive = debit (patient owes). Negative = credit (patient prepaid / overpaid).
        var siblingList = siblings.Count > 0 ? siblings : [invoice];
        var invoiceDue = netInvoice - receiptTotal - paymentTotal;
        var groupOwed = siblingList.Sum(i => i.TotalAmount);
        var groupNet = groupOwed - totalReceived;
        var receiptStoredCredit = matchingReceipts.Sum(GetReceiptUnappliedCredit);
        var patientCredit = Math.Max(receiptStoredCredit, Math.Max(0m, -groupNet));

        decimal endingBalance;
        if (invoiceDue > 0)
            endingBalance = invoiceDue;
        else if (patientCredit > 0)
            endingBalance = -patientCredit;
        else if (groupNet < 0)
            endingBalance = groupNet;
        else
            endingBalance = 0m;

        return new ArTotals(
            receiptTotal,
            paymentTotal,
            totalInvoice,
            invoice.Discount,
            netInvoice,
            endingBalance,
            amountApplied,
            totalReceived,
            patientCredit);
    }

    /// <summary>Cash received minus amounts applied to invoices for this patient and doctor.</summary>
    public static decimal ComputeUnappliedCredit(
        IReadOnlyList<Invoice> siblings,
        IReadOnlyList<CashReceipt> matchingReceipts,
        IReadOnlyList<CashPayment> matchingPayments)
    {
        if (siblings.Count == 0) return 0m;

        var totalCredits = matchingReceipts.Sum(r => r.Amount) + matchingPayments.Sum(p => p.Amount);
        var totalOwed = siblings.Sum(i => i.TotalAmount);
        return Math.Max(0m, totalCredits - totalOwed);
    }

    /// <summary>Net patient-doctor balance: positive = debit owed, negative = credit.</summary>
    public static decimal ComputeGroupNetBalance(
        Invoice invoice,
        IReadOnlyList<Invoice> siblingInvoices,
        IReadOnlyList<CashReceipt> receipts,
        IReadOnlyList<CashPayment> payments)
    {
        var siblings = siblingInvoices.Where(i => SamePatientDoctor(i, invoice)).ToList();
        if (siblings.Count == 0) siblings = [invoice];

        var matchingReceipts = receipts.Where(r => MatchesReceipt(r, invoice)).ToList();
        var matchingPayments = payments.Where(p => MatchesPayment(p, invoice)).ToList();
        var owed = siblings.Sum(i => i.TotalAmount);
        var received = matchingReceipts.Sum(r => r.Amount) + matchingPayments.Sum(p => p.Amount);
        return owed - received;
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

    /// <summary>Unapplied amount from receipt using balance-due snapshot captured at payment time.</summary>
    public static decimal GetReceiptUnappliedCredit(CashReceipt receipt)
    {
        if (receipt.PatientCredit > 0)
            return receipt.PatientCredit;

        if (receipt.BalanceDue <= 0)
            return 0m;

        var applied = Math.Min(receipt.Amount, receipt.BalanceDue);
        return Math.Max(0m, receipt.Amount - applied);
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
        if (!string.IsNullOrWhiteSpace(invoice.PatientId) && !string.IsNullOrWhiteSpace(transactionPatientId))
        {
            var invId = invoice.PatientId.Trim();
            var txId = transactionPatientId.Trim();
            if (string.Equals(txId, invId, StringComparison.OrdinalIgnoreCase)) return true;
            if (txId.Contains(invId, StringComparison.OrdinalIgnoreCase) ||
                invId.Contains(txId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(invoice.PatientName) && !string.IsNullOrWhiteSpace(transactionPatientName))
        {
            var invName = invoice.PatientName.Trim();
            var txName = transactionPatientName.Trim();
            if (string.Equals(invName, txName, StringComparison.OrdinalIgnoreCase)) return true;
            if (invName.Contains(txName, StringComparison.OrdinalIgnoreCase) ||
                txName.Contains(invName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool MatchesDoctor(string? transactionDoctor, string? invoiceDoctor)
    {
        if (string.IsNullOrWhiteSpace(transactionDoctor)) return true;
        if (string.IsNullOrWhiteSpace(invoiceDoctor)) return true;
        var tx = transactionDoctor.Trim();
        var inv = invoiceDoctor.Trim();
        return string.Equals(tx, inv, StringComparison.OrdinalIgnoreCase) ||
               tx.Contains(inv, StringComparison.OrdinalIgnoreCase) ||
               inv.Contains(tx, StringComparison.OrdinalIgnoreCase);
    }

    private static List<CashReceipt> ResolveMatchingReceipts(IReadOnlyList<CashReceipt> receipts, Invoice invoice)
    {
        var strict = receipts.Where(r => MatchesReceipt(r, invoice)).ToList();
        if (strict.Count > 0) return strict;
        return receipts.Where(r => MatchesPatient(r.PatientId, r.PatientName, invoice)).ToList();
    }

    private static List<CashPayment> ResolveMatchingPayments(IReadOnlyList<CashPayment> payments, Invoice invoice)
    {
        var strict = payments.Where(p => MatchesPayment(p, invoice)).ToList();
        if (strict.Count > 0) return strict;
        return payments.Where(p => MatchesPatient(p.PatientId, p.PayeeName, invoice)).ToList();
    }
}
