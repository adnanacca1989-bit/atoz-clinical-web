namespace AtoZClinical.Core.Entities;

public class JournalEntry : IClinicScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int EntryNo { get; set; }
    public DateTime EntryDate { get; set; } = DateTime.Today;
    public string SourceType { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Clinic Clinic { get; set; } = null!;
    public ICollection<JournalEntryLine> Lines { get; set; } = [];
}

public class JournalEntryLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JournalEntryId { get; set; }
    public int LineNo { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string? AccountCategory { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Description { get; set; }

    public JournalEntry JournalEntry { get; set; } = null!;
}
