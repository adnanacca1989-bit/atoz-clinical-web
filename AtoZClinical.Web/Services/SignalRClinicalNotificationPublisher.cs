using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AtoZClinical.Web.Services;

public sealed class SignalRClinicalNotificationPublisher : IClinicalNotificationPublisher
{
    private readonly ClinicalNotificationService _notifications;
    private readonly IHubContext<NotificationHub> _hub;

    public SignalRClinicalNotificationPublisher(
        ClinicalNotificationService notifications,
        IHubContext<NotificationHub> hub)
    {
        _notifications = notifications;
        _hub = hub;
    }

    public async Task PublishDepartmentAsync(
        Guid clinicId,
        string targetRole,
        string title,
        string detail,
        string link,
        CancellationToken ct = default)
    {
        var row = await _notifications.NotifyDepartmentAsync(clinicId, targetRole, title, detail, link, ct);
        var payload = ClinicalNotificationService.ToPayload(row);

        await _hub.Clients.Group(ClinicalNotificationRoles.RoleGroup(clinicId, targetRole))
            .SendAsync("ReceiveNotification", payload, ct);

        if (targetRole.Equals(ClinicalNotificationRoles.Laboratory, StringComparison.OrdinalIgnoreCase))
        {
            await _hub.Clients.Group(ClinicalNotificationRoles.RoleGroup(clinicId, ClinicalNotificationRoles.Lab))
                .SendAsync("ReceiveNotification", payload, ct);
        }
        else if (targetRole.Equals(ClinicalNotificationRoles.Pharmacy, StringComparison.OrdinalIgnoreCase))
        {
            await _hub.Clients.Group(ClinicalNotificationRoles.RoleGroup(clinicId, ClinicalNotificationRoles.Cashier))
                .SendAsync("ReceiveNotification", payload, ct);
        }

        await _hub.Clients.Group(ClinicalNotificationRoles.ClinicGroup(clinicId))
            .SendAsync("ReceiveNotification", payload, ct);
    }
}
