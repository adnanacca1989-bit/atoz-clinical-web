using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;

namespace AtoZClinical.Web.Hubs;

[Authorize]
public sealed class NotificationHub : Hub
{
    private readonly UserManager<ApplicationUser> _users;

    public NotificationHub(UserManager<ApplicationUser> users) => _users = users;

    public override async Task OnConnectedAsync()
    {
        var user = await _users.GetUserAsync(Context.User!);
        if (user?.ClinicId is null)
        {
            Context.Abort();
            return;
        }

        var roleGroup = ClinicalNotificationRoles.SignalRGroupForUser(user);
        if (roleGroup is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, roleGroup);

        await base.OnConnectedAsync();
    }
}
