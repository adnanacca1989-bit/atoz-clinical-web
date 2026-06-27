namespace AtoZClinical.Core.Entities;

public class ClinicMessage : IClinicScoped
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string SenderUserId { get; set; } = string.Empty;
    public string RecipientUserId { get; set; } = string.Empty;
    public string? Body { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
    public Guid? AttachmentId { get; set; }
    public ClinicMessageAttachment? Attachment { get; set; }
}
