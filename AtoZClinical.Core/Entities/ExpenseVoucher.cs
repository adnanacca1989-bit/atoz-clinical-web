namespace AtoZClinical.Core.Entities;

public class ExpenseVoucher : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int ExpenseNo { get; set; }
    public DateTime ExpenseDate { get; set; } = DateTime.Today;
    public string PaymentMethod { get; set; } = "Cash";
    public string? PayeeName { get; set; }
    public string? CreditAccountName { get; set; }
    public string? Description { get; set; }
    public decimal TotalAmount { get; set; }
    public Guid? JournalEntryId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
    public JournalEntry? JournalEntry { get; set; }
    public ICollection<ExpenseVoucherLine> Lines { get; set; } = [];
}

public class ExpenseVoucherLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExpenseVoucherId { get; set; }
    public int LineNo { get; set; }
    public string ChartAccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Description { get; set; }

    public ExpenseVoucher ExpenseVoucher { get; set; } = null!;
}
