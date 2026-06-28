using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Web.Pages.Notifications;

public class FeedModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly ClinicalDbContext _db;
    private readonly AppointmentReminderService _reminders;
    private readonly ClinicalNotificationService _clinicalNotifications;
    private readonly DoctorScopeContext _doctorScope;
    private readonly ILogger<FeedModel> _logger;

    public FeedModel(
        ClinicContextService clinicContext,
        ClinicalDbContext db,
        AppointmentReminderService reminders,
        ClinicalNotificationService clinicalNotifications,
        DoctorScopeContext doctorScope,
        ILogger<FeedModel> logger)
    {
        _clinicContext = clinicContext;
        _db = db;
        _reminders = reminders;
        _clinicalNotifications = clinicalNotifications;
        _doctorScope = doctorScope;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(long? sinceTicks)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var user = await _clinicContext.GetCurrentUserAsync();
        var since = sinceTicks.HasValue
            ? new DateTime(sinceTicks.Value, DateTimeKind.Utc)
            : DateTime.UtcNow.AddDays(-30);

        var items = new List<NotificationPayload>();

        if (user is not null && !_doctorScope.Filter.IsRestricted)
        {
            foreach (var appt in await _reminders.GetActiveNotificationsAsync(clinicId.Value))
            {
                if (appt.AtUtc < since) continue;
                items.Add(new NotificationPayload(appt.Id, appt.Kind, appt.Title, appt.Detail, appt.Link, appt.AtUtc));
            }
        }

        if (user is not null)
        {
            foreach (var n in await _clinicalNotifications.ListForUserSinceAsync(clinicId.Value, user, since))
            {
                items.Add(new NotificationPayload(
                    $"notify-{n.Id:N}",
                    "department",
                    n.Title,
                    n.Detail,
                    n.Link,
                    n.CreatedAt));
            }
        }

        if (user is not null)
        {
            var statusEntries = await _db.AuditLogEntries
                .AsNoTracking()
                .Where(a => a.ClinicId == clinicId && a.DateTime >= since && a.FormName == "Patient Status")
                .OrderByDescending(a => a.DateTime)
                .Take(50)
                .ToListAsync();

            foreach (var entry in statusEntries)
            {
                items.Add(new NotificationPayload(
                    $"audit-{entry.Id:N}",
                    "status",
                    "Patient Status",
                    string.IsNullOrWhiteSpace(entry.Details) ? "Patient status updated" : entry.Details,
                    "/Reports/PatientStatus",
                    entry.DateTime.ToUniversalTime()));
            }
        }

        _logger.LogDebug(
            "Notification feed for user {User}: {Count} items (restricted={Restricted})",
            user?.UserName, items.Count, _doctorScope.Filter.IsRestricted);

        var ordered = items
            .OrderByDescending(i => i.AtUtc)
            .Take(50)
            .Select(i => i.ToJson())
            .ToList();

        return new JsonResult(new
        {
            serverTime = DateTime.UtcNow.Ticks,
            items = ordered
        });
    }

    private sealed record NotificationPayload(
        string Id,
        string Kind,
        string Title,
        string Detail,
        string Link,
        DateTime AtUtc)
    {
        public object ToJson() => new
        {
            id = Id,
            kind = Kind,
            title = Title,
            detail = Detail,
            link = Link,
            at = AtUtc.ToLocalTime().ToString("g"),
            atUtc = AtUtc.Ticks
        };
    }
}
