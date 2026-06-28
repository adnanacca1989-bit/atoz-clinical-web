using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Web.Services;

public sealed class SignalRClinicalNotificationPublisher : IClinicalNotificationPublisher
{
    private readonly ClinicalNotificationService _notifications;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<SignalRClinicalNotificationPublisher> _logger;

    public SignalRClinicalNotificationPublisher(
        ClinicalNotificationService notifications,
        IHubContext<NotificationHub> hub,
        ILogger<SignalRClinicalNotificationPublisher> logger)
    {
        _notifications = notifications;
        _hub = hub;
        _logger = logger;
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

        await _hub.Clients.Group(ClinicalNotificationRoles.ClinicGroup(clinicId))
            .SendAsync("ReceiveNotification", payload, ct);

        _logger.LogDebug(
            "Published department notification {Role} to clinic {ClinicId}: {Title}",
            targetRole, clinicId, title);
    }
}
