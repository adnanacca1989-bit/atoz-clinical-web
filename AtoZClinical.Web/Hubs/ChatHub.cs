using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;

namespace AtoZClinical.Web.Hubs;

[Authorize]
public sealed class ChatHub : Hub
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ClinicMessagingService _messaging;
    private readonly ChatPresenceService _presence;

    public ChatHub(
        UserManager<ApplicationUser> users,
        ClinicMessagingService messaging,
        ChatPresenceService presence)
    {
        _users = users;
        _messaging = messaging;
        _presence = presence;
    }

    public override async Task OnConnectedAsync()
    {
        var user = await GetCurrentUserAsync();
        if (user?.ClinicId is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ClinicGroup(user.ClinicId.Value));
        _presence.UserConnected(user.ClinicId.Value, user.Id, Context.ConnectionId);
        await BroadcastPresenceAsync(user.ClinicId.Value);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var user = await GetCurrentUserAsync();
        if (user?.ClinicId is not null)
        {
            _presence.UserDisconnected(user.ClinicId.Value, user.Id, Context.ConnectionId);
            await BroadcastPresenceAsync(user.ClinicId.Value);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendMessage(string recipientUserId, string? body, Guid? attachmentId)
    {
        var user = await GetCurrentUserAsync();
        if (user?.ClinicId is null) return;

        var message = await _messaging.SendMessageAsync(
            user.ClinicId.Value, user.Id, recipientUserId, body, attachmentId);
        if (message is null)
            throw new HubException("Message could not be sent. Check the recipient is in your clinic.");

        await Clients.User(recipientUserId).SendAsync("ReceiveMessage", ChatPayloadFormatter.ToPayload(message, recipientUserId));
        await Clients.Caller.SendAsync("ReceiveMessage", ChatPayloadFormatter.ToPayload(message, user.Id));
    }

    public async Task MarkRead(string peerUserId)
    {
        var user = await GetCurrentUserAsync();
        if (user?.ClinicId is null) return;

        await _messaging.MarkConversationReadAsync(user.ClinicId.Value, user.Id, peerUserId);
        await Clients.User(peerUserId).SendAsync("MessagesRead", user.Id);
    }

    private async Task BroadcastPresenceAsync(Guid clinicId)
    {
        var online = _presence.GetOnlineUserIds(clinicId);
        await Clients.Group(ClinicGroup(clinicId)).SendAsync("PresenceChanged", online);
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync() =>
        await _users.GetUserAsync(Context.User!);

    private static string ClinicGroup(Guid clinicId) => $"clinic-{clinicId:N}";
}
