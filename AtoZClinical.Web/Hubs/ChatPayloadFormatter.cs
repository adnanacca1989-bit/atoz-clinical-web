using AtoZClinical.Infrastructure.Services;

namespace AtoZClinical.Web.Hubs;

public static class ChatPayloadFormatter
{
    public static object ToPayload(ChatMessageDto message, string viewerUserId) => new
    {
        id = message.Id,
        senderUserId = message.SenderUserId,
        recipientUserId = message.RecipientUserId,
        body = message.Body,
        sentAt = message.SentAt,
        readAt = message.ReadAt,
        attachmentId = message.AttachmentId,
        attachmentFileName = message.AttachmentFileName,
        attachmentContentType = message.AttachmentContentType,
        isMine = message.SenderUserId == viewerUserId
    };
}
