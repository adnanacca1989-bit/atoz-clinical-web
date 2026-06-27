namespace AtoZClinical.Core.Entities;

public class ClinicMessageAttachment : IClinicScoped
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public int FileSize { get; set; }
    public byte[] Data { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
