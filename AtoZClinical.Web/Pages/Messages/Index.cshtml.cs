using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Hubs;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;

namespace AtoZClinical.Web.Pages.Messages;

public class IndexModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly ClinicMessagingService _messaging;
    private readonly ChatPresenceService _presence;
    private readonly IHubContext<ChatHub> _hub;

    public IndexModel(
        ClinicContextService clinicContext,
        ClinicMessagingService messaging,
        ChatPresenceService presence,
        IHubContext<ChatHub> hub)
    {
        _clinicContext = clinicContext;
        _messaging = messaging;
        _presence = presence;
        _hub = hub;
    }

    public string CurrentUserId { get; private set; } = "";
    public string CurrentUserName { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _clinicContext.GetCurrentUserAsync();
        if (user?.ClinicId is null) return Forbid();

        CurrentUserId = user.Id;
        CurrentUserName = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName ?? "" : user.FullName;
        return Page();
    }

    public async Task<IActionResult> OnGetUsersAsync()
    {
        var user = await _clinicContext.GetCurrentUserAsync();
        if (user?.ClinicId is null) return Forbid();

        var users = await _messaging.GetClinicUsersAsync(user.ClinicId.Value, user.Id);
        var online = _presence.GetOnlineUserIds(user.ClinicId.Value).ToHashSet(StringComparer.Ordinal);

        return new JsonResult(users.Select(u => new
        {
            userId = u.UserId,
            name = u.Name,
            role = u.Role,
            unreadCount = u.UnreadCount,
            lastMessageAt = u.LastMessageAt,
            lastPreview = u.LastPreview,
            isOnline = online.Contains(u.UserId)
        }));
    }

    public async Task<IActionResult> OnGetHistoryAsync(string peerUserId, DateTime? before, int take = 100)
    {
        var user = await _clinicContext.GetCurrentUserAsync();
        if (user?.ClinicId is null) return Forbid();
        if (string.IsNullOrWhiteSpace(peerUserId)) return BadRequest();

        var messages = await _messaging.GetConversationAsync(
            user.ClinicId.Value, user.Id, peerUserId, take, before);

        await _messaging.MarkConversationReadAsync(user.ClinicId.Value, user.Id, peerUserId);

        return new JsonResult(messages.Select(m => new
        {
            id = m.Id,
            senderUserId = m.SenderUserId,
            recipientUserId = m.RecipientUserId,
            body = m.Body,
            sentAt = m.SentAt,
            readAt = m.ReadAt,
            attachmentId = m.AttachmentId,
            attachmentFileName = m.AttachmentFileName,
            attachmentContentType = m.AttachmentContentType,
            isMine = m.IsMine
        }));
    }

    public async Task<IActionResult> OnGetUnreadAsync()
    {
        var user = await _clinicContext.GetCurrentUserAsync();
        if (user?.ClinicId is null) return Forbid();

        var count = await _messaging.GetUnreadCountAsync(user.ClinicId.Value, user.Id);
        return new JsonResult(new { count });
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file)
    {
        var user = await _clinicContext.GetCurrentUserAsync();
        if (user?.ClinicId is null) return Forbid();
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file selected." });
        if (file.Length > ClinicMessagingService.MaxAttachmentBytes)
            return BadRequest(new { error = "File exceeds 5 MB limit." });

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var id = await _messaging.SaveAttachmentAsync(
            user.ClinicId.Value,
            user.Id,
            file.FileName,
            file.ContentType,
            ms.ToArray());

        if (id is null)
            return BadRequest(new { error = "Unsupported file type. Use PDF, Excel, or images." });

        return new JsonResult(new
        {
            attachmentId = id,
            fileName = file.FileName,
            contentType = file.ContentType
        });
    }

    public async Task<IActionResult> OnPostSendAsync(
        [FromForm] string recipientUserId,
        [FromForm] string? body,
        [FromForm] Guid? attachmentId)
    {
        var user = await _clinicContext.GetCurrentUserAsync();
        if (user?.ClinicId is null) return Forbid();
        if (string.IsNullOrWhiteSpace(recipientUserId)) return BadRequest(new { error = "Recipient is required." });

        var message = await _messaging.SendMessageAsync(
            user.ClinicId.Value, user.Id, recipientUserId, body, attachmentId);

        if (message is null)
            return BadRequest(new { error = "Could not send message. Check the recipient is in your clinic." });

        var recipientPayload = ChatPayloadFormatter.ToPayload(message, recipientUserId);
        var senderPayload = ChatPayloadFormatter.ToPayload(message, user.Id);

        await _hub.Clients.User(recipientUserId).SendAsync("ReceiveMessage", recipientPayload);
        await _hub.Clients.User(user.Id).SendAsync("ReceiveMessage", senderPayload);

        return new JsonResult(senderPayload);
    }

    public async Task<IActionResult> OnGetDownloadAsync(Guid id)
    {
        var user = await _clinicContext.GetCurrentUserAsync();
        if (user?.ClinicId is null) return Forbid();

        var attachment = await _messaging.GetAttachmentAsync(user.ClinicId.Value, user.Id, id);
        if (attachment is null) return NotFound();

        return File(attachment.Data, attachment.ContentType, attachment.FileName);
    }

    public async Task<IActionResult> OnPostMarkReadAsync(string peerUserId)
    {
        var user = await _clinicContext.GetCurrentUserAsync();
        if (user?.ClinicId is null) return Forbid();
        if (string.IsNullOrWhiteSpace(peerUserId)) return BadRequest();

        await _messaging.MarkConversationReadAsync(user.ClinicId.Value, user.Id, peerUserId);
        return new JsonResult(new { ok = true });
    }
}
