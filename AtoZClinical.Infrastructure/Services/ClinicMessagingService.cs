using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class ClinicMessagingService
{
    public const int MaxAttachmentBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".xls", ".xlsx"
    };

    private readonly ClinicalDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public ClinicMessagingService(ClinicalDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public async Task<List<ChatUserDto>> GetClinicUsersAsync(Guid clinicId, string currentUserId)
    {
        var users = await _users.Users
            .AsNoTracking()
            .Where(u => u.ClinicId == clinicId && u.IsActive && u.Id != currentUserId)
            .OrderBy(u => u.FullName)
            .ThenBy(u => u.UserName)
            .ToListAsync();

        var unread = await _db.ClinicMessages
            .AsNoTracking()
            .Where(m => m.ClinicId == clinicId && m.RecipientUserId == currentUserId && m.ReadAt == null)
            .GroupBy(m => m.SenderUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync();

        var unreadMap = unread.ToDictionary(x => x.UserId, x => x.Count, StringComparer.Ordinal);

        var lastMessages = await _db.ClinicMessages
            .AsNoTracking()
            .Where(m => m.ClinicId == clinicId &&
                        (m.SenderUserId == currentUserId || m.RecipientUserId == currentUserId))
            .OrderByDescending(m => m.SentAt)
            .Take(500)
            .ToListAsync();

        var lastByPeer = new Dictionary<string, ClinicMessage>(StringComparer.Ordinal);
        foreach (var msg in lastMessages)
        {
            var peer = msg.SenderUserId == currentUserId ? msg.RecipientUserId : msg.SenderUserId;
            if (!lastByPeer.ContainsKey(peer))
                lastByPeer[peer] = msg;
        }

        return users.Select(u =>
        {
            lastByPeer.TryGetValue(u.Id, out var last);
            unreadMap.TryGetValue(u.Id, out var unreadCount);
            return new ChatUserDto(
                u.Id,
                DisplayName(u),
                u.ClinicRole?.ToString() ?? "",
                unreadCount,
                last?.SentAt,
                Preview(last));
        }).OrderByDescending(u => u.LastMessageAt ?? DateTime.MinValue).ThenBy(u => u.Name).ToList();
    }

    public async Task<List<ChatMessageDto>> GetConversationAsync(
        Guid clinicId, string currentUserId, string peerUserId, int take = 100, DateTime? before = null)
    {
        if (!await SameClinicAsync(clinicId, peerUserId))
            return [];

        var query = _db.ClinicMessages
            .AsNoTracking()
            .Include(m => m.Attachment)
            .Where(m => m.ClinicId == clinicId &&
                        ((m.SenderUserId == currentUserId && m.RecipientUserId == peerUserId) ||
                         (m.SenderUserId == peerUserId && m.RecipientUserId == currentUserId)));

        if (before.HasValue)
            query = query.Where(m => m.SentAt < before.Value);

        var messages = await query
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync();

        return messages
            .OrderBy(m => m.SentAt)
            .Select(m => ToDto(m, currentUserId))
            .ToList();
    }

    public async Task<ChatMessageDto?> SendMessageAsync(
        Guid clinicId, string senderUserId, string recipientUserId, string? body, Guid? attachmentId)
    {
        if (string.IsNullOrWhiteSpace(body) && attachmentId is null)
            return null;

        if (!await SameClinicAsync(clinicId, recipientUserId))
            return null;

        if (attachmentId.HasValue)
        {
            var attachment = await _db.ClinicMessageAttachments
                .FirstOrDefaultAsync(a => a.Id == attachmentId && a.ClinicId == clinicId && a.UploadedByUserId == senderUserId);
            if (attachment is null)
                return null;
        }

        var message = new ClinicMessage
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            SenderUserId = senderUserId,
            RecipientUserId = recipientUserId,
            Body = string.IsNullOrWhiteSpace(body) ? null : body.Trim(),
            SentAt = DateTime.UtcNow,
            AttachmentId = attachmentId
        };

        _db.ClinicMessages.Add(message);
        await _db.SaveChangesAsync();

        await _db.Entry(message).Reference(m => m.Attachment).LoadAsync();
        return ToDto(message, senderUserId);
    }

    public async Task<int> GetUnreadCountAsync(Guid clinicId, string userId) =>
        await _db.ClinicMessages.CountAsync(m =>
            m.ClinicId == clinicId && m.RecipientUserId == userId && m.ReadAt == null);

    public async Task MarkConversationReadAsync(Guid clinicId, string currentUserId, string peerUserId)
    {
        var unread = await _db.ClinicMessages
            .Where(m => m.ClinicId == clinicId &&
                        m.SenderUserId == peerUserId &&
                        m.RecipientUserId == currentUserId &&
                        m.ReadAt == null)
            .ToListAsync();

        if (unread.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var msg in unread)
            msg.ReadAt = now;

        await _db.SaveChangesAsync();
    }

    public async Task<Guid?> SaveAttachmentAsync(Guid clinicId, string userId, string fileName, string contentType, byte[] data)
    {
        if (data.Length == 0 || data.Length > MaxAttachmentBytes)
            return null;

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensions.Contains(ext))
            return null;

        if (!IsAllowedContentType(contentType, ext))
            return null;

        var attachment = new ClinicMessageAttachment
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            UploadedByUserId = userId,
            FileName = Path.GetFileName(fileName),
            ContentType = contentType,
            FileSize = data.Length,
            Data = data,
            CreatedAt = DateTime.UtcNow
        };

        _db.ClinicMessageAttachments.Add(attachment);
        await _db.SaveChangesAsync();
        return attachment.Id;
    }

    public async Task<ClinicMessageAttachment?> GetAttachmentAsync(Guid clinicId, string userId, Guid attachmentId)
    {
        var attachment = await _db.ClinicMessageAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.ClinicId == clinicId);

        if (attachment is null) return null;

        var linked = await _db.ClinicMessages.AnyAsync(m =>
            m.ClinicId == clinicId &&
            m.AttachmentId == attachmentId &&
            (m.SenderUserId == userId || m.RecipientUserId == userId));

        return linked ? attachment : null;
    }

    private async Task<bool> SameClinicAsync(Guid clinicId, string userId)
    {
        var user = await _users.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        return user?.ClinicId == clinicId && user.IsActive;
    }

    private static string DisplayName(ApplicationUser user) =>
        string.IsNullOrWhiteSpace(user.FullName) ? user.UserName ?? user.Email ?? user.Id : user.FullName;

    private static string? Preview(ClinicMessage? message)
    {
        if (message is null) return null;
        if (!string.IsNullOrWhiteSpace(message.Body)) return message.Body;
        return message.AttachmentId.HasValue ? "📎 Attachment" : null;
    }

    private static ChatMessageDto ToDto(ClinicMessage message, string currentUserId) =>
        new(
            message.Id,
            message.SenderUserId,
            message.RecipientUserId,
            message.Body,
            message.SentAt,
            message.ReadAt,
            message.AttachmentId,
            message.Attachment?.FileName,
            message.Attachment?.ContentType,
            message.SenderUserId == currentUserId);

    private static bool IsAllowedContentType(string contentType, string ext)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;

        return contentType.ToLowerInvariant() switch
        {
            "application/pdf" => ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase),
            "application/vnd.ms-excel" => ext.Equals(".xls", StringComparison.OrdinalIgnoreCase),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase),
            "application/octet-stream" => AllowedExtensions.Contains(ext),
            _ => false
        };
    }
}

public sealed record ChatUserDto(
    string UserId,
    string Name,
    string Role,
    int UnreadCount,
    DateTime? LastMessageAt,
    string? LastPreview);

public sealed record ChatMessageDto(
    Guid Id,
    string SenderUserId,
    string RecipientUserId,
    string? Body,
    DateTime SentAt,
    DateTime? ReadAt,
    Guid? AttachmentId,
    string? AttachmentFileName,
    string? AttachmentContentType,
    bool IsMine);
