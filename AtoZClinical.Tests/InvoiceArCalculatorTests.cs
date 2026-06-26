using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;

namespace AtoZClinical.Tests;

public class InvoiceArCalculatorTests
{
    [Fact]
    public void ForInvoice_caps_cash_receipt_to_amount_applied_to_invoice()
    {
        var clinicId = Guid.NewGuid();
        var invoice = new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 2,
            InvoiceDate = new DateTime(2026, 6, 24),
            PatientName = "Falah Hadi",
            PatientId = "PAT-00003",
            DoctorName = "Mohammed Adnan",
            SubTotal = 65_000,
            TotalAmount = 65_000,
            AmountPaid = 65_000,
            BalanceDue = 0
        };

        var receipts = new List<CashReceipt>
        {
            new()
            {
                ClinicId = clinicId,
                ReceiptNo = 1,
                ReceiptDate = invoice.InvoiceDate,
                PatientName = invoice.PatientName,
                PatientId = invoice.PatientId,
                DoctorName = invoice.DoctorName,
                Amount = 70_000
            }
        };

        var totals = InvoiceArCalculator.ForInvoice(invoice, receipts, [], [invoice]);

        Assert.Equal(65_000, totals.CashReceipt);
        Assert.Equal(70_000, totals.TotalReceived);
        Assert.Equal(5_000, totals.PatientCredit);
        Assert.Equal(-5_000, totals.EndingBalance);
        Assert.Equal(65_000, totals.AmountApplied);
    }

    [Fact]
    public void ForInvoice_shows_debit_balance_when_invoice_unpaid()
    {
        var clinicId = Guid.NewGuid();
        var invoice = new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 1,
            InvoiceDate = new DateTime(2026, 6, 25),
            PatientName = "Test Patient",
            TotalAmount = 50_000,
            AmountPaid = 0,
            BalanceDue = 50_000
        };

        var totals = InvoiceArCalculator.ForInvoice(invoice, [], [], [invoice]);

        Assert.Equal(50_000, totals.EndingBalance);
        Assert.Equal(0, totals.PatientCredit);
    }

    [Fact]
    public void ForInvoice_shows_patient_credit_when_overpayment_exceeds_invoice()
    {
        var clinicId = Guid.NewGuid();
        var invoice = new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 1,
            InvoiceDate = new DateTime(2026, 6, 25),
            PatientName = "Noor Alaa",
            PatientId = "PAT-00001",
            DoctorName = "Muslem Essa",
            SubTotal = 150_000,
            TotalAmount = 150_000,
            AmountPaid = 150_000,
            BalanceDue = 0
        };

        var receipts = new List<CashReceipt>
        {
            new()
            {
                ClinicId = clinicId,
                ReceiptNo = 1,
                ReceiptDate = invoice.InvoiceDate,
                PatientName = invoice.PatientName,
                PatientId = invoice.PatientId,
                DoctorName = invoice.DoctorName,
                Amount = 1_000_000
            }
        };

        var totals = InvoiceArCalculator.ForInvoice(invoice, receipts, [], [invoice]);

        Assert.Equal(150_000, totals.CashReceipt);
        Assert.Equal(1_000_000, totals.TotalReceived);
        Assert.Equal(850_000, totals.PatientCredit);
        Assert.Equal(-850_000, totals.EndingBalance);
    }

    [Fact]
    public void ForInvoice_allocates_receipts_across_sibling_invoices_in_order()
    {
        var clinicId = Guid.NewGuid();
        var inv1 = new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 1,
            InvoiceDate = new DateTime(2026, 6, 24),
            PatientName = "Falah Hadi",
            DoctorName = "Mohammed Adnan",
            SubTotal = 40_000,
            TotalAmount = 40_000,
            AmountPaid = 40_000,
            BalanceDue = 0
        };
        var inv2 = new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 2,
            InvoiceDate = new DateTime(2026, 6, 25),
            PatientName = "Falah Hadi",
            DoctorName = "Mohammed Adnan",
            SubTotal = 25_000,
            TotalAmount = 25_000,
            AmountPaid = 25_000,
            BalanceDue = 0
        };

        var receipts = new List<CashReceipt>
        {
            new()
            {
                ClinicId = clinicId,
                ReceiptNo = 1,
                ReceiptDate = inv1.InvoiceDate,
                PatientName = inv1.PatientName,
                DoctorName = inv1.DoctorName,
                Amount = 70_000
            }
        };

        var siblings = new List<Invoice> { inv1, inv2 };
        var t1 = InvoiceArCalculator.ForInvoice(inv1, receipts, [], siblings);
        var t2 = InvoiceArCalculator.ForInvoice(inv2, receipts, [], siblings);

        Assert.Equal(40_000, t1.CashReceipt);
        Assert.Equal(25_000, t2.CashReceipt);
        Assert.Equal(-5_000, t1.EndingBalance);
        Assert.Equal(-5_000, t2.EndingBalance);
    }

    [Fact]
    public void ForInvoice_shows_credit_on_earlier_invoice_when_later_sibling_exists()
    {
        var clinicId = Guid.NewGuid();
        var inv1 = new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 1,
            InvoiceDate = new DateTime(2026, 6, 24),
            PatientName = "Noor Alaa",
            PatientId = "PAT-00001",
            DoctorName = "Muslem Essa",
            TotalAmount = 150_000,
            AmountPaid = 150_000,
            BalanceDue = 0
        };
        var inv2 = new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 2,
            InvoiceDate = new DateTime(2026, 6, 25),
            PatientName = "Noor Alaa",
            PatientId = "PAT-00001",
            DoctorName = "Muslem Essa",
            TotalAmount = 50_000,
            AmountPaid = 50_000,
            BalanceDue = 0
        };

        var receipts = new List<CashReceipt>
        {
            new()
            {
                ClinicId = clinicId,
                ReceiptNo = 1,
                ReceiptDate = inv1.InvoiceDate,
                PatientName = inv1.PatientName,
                PatientId = inv1.PatientId,
                DoctorName = inv1.DoctorName,
                Amount = 1_000_000
            }
        };

        var siblings = new List<Invoice> { inv1, inv2 };
        var firstInvoiceTotals = InvoiceArCalculator.ForInvoice(inv1, receipts, [], siblings);

        Assert.Equal(150_000, firstInvoiceTotals.CashReceipt);
        Assert.Equal(1_000_000, firstInvoiceTotals.TotalReceived);
        Assert.Equal(800_000, firstInvoiceTotals.PatientCredit);
        Assert.Equal(-800_000, firstInvoiceTotals.EndingBalance);
    }

    [Fact]
    public void ForInvoice_patient_credit_zero_when_receipts_do_not_exceed_group_invoices()
    {
        var clinicId = Guid.NewGuid();
        var invoices = Enumerable.Range(1, 7).Select(n => new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = n,
            InvoiceDate = new DateTime(2026, 6, n),
            PatientName = "Noor Alaa",
            PatientId = "PAT-00001",
            DoctorName = "Muslem Essa",
            TotalAmount = 150_000,
            AmountPaid = 150_000,
            BalanceDue = 0
        }).ToList();

        var receipts = new List<CashReceipt>
        {
            new()
            {
                ClinicId = clinicId,
                ReceiptNo = 1,
                ReceiptDate = new DateTime(2026, 6, 25),
                PatientName = "Noor Alaa",
                PatientId = "PAT-00001",
                DoctorName = "Muslem Essa",
                Amount = 1_000_000,
                BalanceDue = 150_000,
                PatientCredit = 850_000
            }
        };

        var totals = InvoiceArCalculator.ForInvoice(invoices[0], receipts, [], invoices);

        Assert.Equal(0, totals.PatientCredit);
        Assert.Equal(0, totals.EndingBalance);
    }

    [Fact]
    public void ForInvoice_zeros_balance_when_refund_matches_patient_credit()
    {
        var clinicId = Guid.NewGuid();
        var invoice = new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 1,
            InvoiceDate = new DateTime(2026, 6, 26),
            PatientName = "adnan jafari",
            PatientId = "PAT-00001",
            DoctorName = "Mohammed Adnan",
            SubTotal = 25_000,
            TotalAmount = 25_000,
            AmountPaid = 25_000,
            BalanceDue = 0
        };

        var receipts = new List<CashReceipt>
        {
            new()
            {
                ClinicId = clinicId,
                ReceiptNo = 1,
                ReceiptDate = invoice.InvoiceDate,
                PatientName = invoice.PatientName,
                PatientId = invoice.PatientId,
                DoctorName = invoice.DoctorName,
                Amount = 100_000,
                PatientCredit = 75_000
            }
        };

        var payments = new List<CashPayment>
        {
            new()
            {
                ClinicId = clinicId,
                PaymentNo = 2,
                PaymentDate = invoice.InvoiceDate,
                PayeeName = invoice.PatientName,
                PatientId = invoice.PatientId,
                DoctorName = invoice.DoctorName,
                Amount = 75_000
            }
        };

        var totals = InvoiceArCalculator.ForInvoice(invoice, receipts, payments, [invoice]);

        Assert.Equal(25_000, totals.CashReceipt);
        Assert.Equal(75_000, totals.CashPayment);
        Assert.Equal(100_000, totals.TotalReceived);
        Assert.Equal(0, totals.PatientCredit);
        Assert.Equal(0, totals.EndingBalance);
    }

    [Fact]
    public void ComputeTotalPatientCredit_is_zero_after_refund_offsets_overpayment()
    {
        var clinicId = Guid.NewGuid();
        var invoice = new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 1,
            InvoiceDate = new DateTime(2026, 6, 26),
            PatientName = "adnan jafari",
            PatientId = "PAT-00001",
            DoctorName = "Mohammed Adnan",
            TotalAmount = 25_000
        };

        var receipts = new List<CashReceipt>
        {
            new()
            {
                ClinicId = clinicId,
                ReceiptNo = 1,
                PatientName = invoice.PatientName,
                PatientId = invoice.PatientId,
                DoctorName = invoice.DoctorName,
                Amount = 100_000,
                PatientCredit = 75_000
            }
        };

        var payments = new List<CashPayment>
        {
            new()
            {
                ClinicId = clinicId,
                PaymentNo = 2,
                PayeeName = invoice.PatientName,
                PatientId = invoice.PatientId,
                DoctorName = invoice.DoctorName,
                Amount = 75_000
            }
        };

        var credit = InvoiceArCalculator.ComputeTotalPatientCredit([invoice], receipts, payments);
        Assert.Equal(0, credit);
    }

    [Fact]
    public void ForInvoice_separates_receipts_and_payments_by_doctor()
    {
        var clinicId = Guid.NewGuid();
        var invEnt = new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 1,
            InvoiceDate = new DateTime(2026, 6, 26),
            PatientName = "Jones Elia",
            PatientId = "PAT-00002",
            DoctorName = "Mohammed Adnan Karar",
            Specialty = "ENT",
            SubTotal = 25_000,
            TotalAmount = 25_000,
            AmountPaid = 25_000,
            BalanceDue = 0
        };
        var invDental = new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 2,
            InvoiceDate = new DateTime(2026, 6, 26),
            PatientName = "Jones Elia",
            PatientId = "PAT-00002",
            DoctorName = "Baneen Mohanad",
            Specialty = "Dental",
            SubTotal = 95_000,
            TotalAmount = 95_000,
            AmountPaid = 95_000,
            BalanceDue = 0
        };

        var receipts = new List<CashReceipt>
        {
            new()
            {
                ClinicId = clinicId,
                ReceiptNo = 1,
                ReceiptDate = invEnt.InvoiceDate,
                PatientName = invEnt.PatientName,
                PatientId = invEnt.PatientId,
                DoctorName = invEnt.DoctorName,
                Amount = 25_000
            },
            new()
            {
                ClinicId = clinicId,
                ReceiptNo = 2,
                ReceiptDate = invDental.InvoiceDate,
                PatientName = invDental.PatientName,
                PatientId = invDental.PatientId,
                DoctorName = invDental.DoctorName,
                Amount = 100_000
            }
        };

        var payments = new List<CashPayment>
        {
            new()
            {
                ClinicId = clinicId,
                PaymentNo = 1,
                PaymentDate = invEnt.InvoiceDate,
                PayeeName = invEnt.PatientName,
                PatientId = invEnt.PatientId,
                DoctorName = invEnt.DoctorName,
                Amount = 10_000
            }
        };

        var siblings = new List<Invoice> { invEnt, invDental };
        var dentalTotals = InvoiceArCalculator.ForInvoice(invDental, receipts, payments, siblings);

        Assert.Equal(0, dentalTotals.CashPayment);
        Assert.Equal(95_000, dentalTotals.CashReceipt);
        Assert.Equal(100_000, dentalTotals.TotalReceived);
        Assert.Equal(5_000, dentalTotals.PatientCredit);
        Assert.Equal(-5_000, dentalTotals.EndingBalance);

        var entTotals = InvoiceArCalculator.ForInvoice(invEnt, receipts, payments, siblings);
        Assert.Equal(10_000, entTotals.CashPayment);
        Assert.Equal(25_000, entTotals.CashReceipt);
    }

    [Fact]
    public void ForInvoice_patient_credit_is_overpayment_not_gross_receipt()
    {
        var clinicId = Guid.NewGuid();
        var invoice = new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 4,
            InvoiceDate = new DateTime(2026, 6, 26),
            PatientName = "Jones Elia",
            PatientId = "PAT-00002",
            DoctorName = "Ayman Hassan",
            SubTotal = 50_000,
            TotalAmount = 50_000,
            AmountPaid = 50_000,
            BalanceDue = 0
        };

        var receipts = new List<CashReceipt>
        {
            new()
            {
                ClinicId = clinicId,
                ReceiptNo = 1,
                ReceiptDate = invoice.InvoiceDate,
                PatientName = invoice.PatientName,
                PatientId = invoice.PatientId,
                DoctorName = invoice.DoctorName,
                Amount = 60_000,
                PatientCredit = 60_000,
                BalanceDue = 0
            }
        };

        var totals = InvoiceArCalculator.ForInvoice(invoice, receipts, [], [invoice]);

        Assert.Equal(50_000, totals.CashReceipt);
        Assert.Equal(60_000, totals.TotalReceived);
        Assert.Equal(10_000, totals.PatientCredit);
        Assert.Equal(-10_000, totals.EndingBalance);
    }

    [Fact]
    public void ForInvoice_patient_credit_zero_after_refund_to_patient()
    {
        var clinicId = Guid.NewGuid();
        var invoice = new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 4,
            InvoiceDate = new DateTime(2026, 6, 26),
            PatientName = "Jones Elia",
            DoctorName = "Ayman Hassan",
            SubTotal = 50_000,
            TotalAmount = 50_000,
            AmountPaid = 50_000,
            BalanceDue = 0
        };

        var receipts = new List<CashReceipt>
        {
            new()
            {
                ClinicId = clinicId,
                ReceiptNo = 1,
                ReceiptDate = invoice.InvoiceDate,
                PatientName = invoice.PatientName,
                DoctorName = invoice.DoctorName,
                Amount = 60_000
            }
        };

        var payments = new List<CashPayment>
        {
            new()
            {
                ClinicId = clinicId,
                PaymentNo = 1,
                PaymentDate = invoice.InvoiceDate,
                PayeeName = invoice.PatientName,
                DoctorName = invoice.DoctorName,
                Amount = 10_000
            }
        };

        var totals = InvoiceArCalculator.ForInvoice(invoice, receipts, payments, [invoice]);

        Assert.Equal(10_000, totals.CashPayment);
        Assert.Equal(0, totals.PatientCredit);
        Assert.Equal(0, totals.EndingBalance);
    }
}
