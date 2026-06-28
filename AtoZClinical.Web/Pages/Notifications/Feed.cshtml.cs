using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Notifications;

public class FeedModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly ClinicalDbContext _db;
    private readonly AppointmentReminderService _reminders;
    private readonly ClinicalNotificationService _clinicalNotifications;
    private readonly DoctorScopeContext _doctorScope;

    private static readonly Dictionary<string, (string Title, string Link)> FormMeta = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Invoice"] = ("Invoice Billing", "/Invoices"),
        ["Radiology Request"] = ("Radiology Request", "/Radiology/Request"),
        ["Radiology Result"] = ("Radiology Result", "/Radiology/Result"),
        ["Prescription"] = ("Doctor's Prescription", "/Prescriptions"),
        ["Laboratory Request"] = ("Laboratory Request", "/Laboratory/Request"),
        ["Laboratory Result"] = ("Laboratory Result", "/Laboratory/Result"),
        ["Pharmacy Bill"] = ("Pharmacy Bill", "/Pharmacy/Bill"),
        ["Pharmacy Request"] = ("Pharmacy Request", "/Pharmacy/Request"),
        ["Patient Status"] = ("Patient Status", "/Reports/PatientStatus")
    };

    public FeedModel(
        ClinicContextService clinicContext,
        ClinicalDbContext db,
        AppointmentReminderService reminders,
        ClinicalNotificationService clinicalNotifications,
        DoctorScopeContext doctorScope)
    {
        _clinicContext = clinicContext;
        _db = db;
        _reminders = reminders;
        _clinicalNotifications = clinicalNotifications;
        _doctorScope = doctorScope;
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

        foreach (var appt in await _reminders.GetActiveNotificationsAsync(clinicId.Value))
        {
            if (appt.AtUtc < since) continue;
            items.Add(new NotificationPayload(appt.Id, appt.Kind, appt.Title, appt.Detail, appt.Link, appt.AtUtc));
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

        var auditEntries = await _db.AuditLogEntries
            .AsNoTracking()
            .Where(a => a.ClinicId == clinicId && a.DateTime >= since &&
                        (a.Type == "Create" || a.FormName == "Patient Status"))
            .OrderByDescending(a => a.DateTime)
            .Take(100)
            .ToListAsync();

        foreach (var entry in auditEntries)
        {
            var formName = entry.FormName ?? "";
            var meta = FormMeta.FirstOrDefault(kv => formName.Contains(kv.Key, StringComparison.OrdinalIgnoreCase));
            if (meta.Key is null) continue;

            var detail = string.IsNullOrWhiteSpace(entry.Details) ? $"{meta.Value.Title} updated" : entry.Details;
            var kind = formName.Contains("Patient Status", StringComparison.OrdinalIgnoreCase) ? "status" : "activity";
            items.Add(new NotificationPayload(
                $"audit-{entry.Id:N}",
                kind,
                meta.Value.Title,
                detail,
                meta.Value.Link,
                entry.DateTime.ToUniversalTime()));
        }

        await AddLabNotificationsAsync(_db, clinicId.Value, since, items);

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

    private async Task AddLabNotificationsAsync(ClinicalDbContext db, Guid clinicId, DateTime since, List<NotificationPayload> items)
    {
        if (_doctorScope.Filter.IsRestricted)
            return;

        var requests = await db.LabRequests
            .AsNoTracking()
            .Where(r => r.ClinicId == clinicId && r.CreatedAt >= since)
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .ToListAsync();

        foreach (var r in requests)
        {
            items.Add(new NotificationPayload(
                $"lab-req-{r.Id:N}",
                "activity",
                "Laboratory Request",
                $"Request #{r.RequestNo} — {r.PatientName}",
                "/Laboratory/Request",
                r.CreatedAt));
        }

        var results = await db.LabResults
            .AsNoTracking()
            .Where(r => r.ClinicId == clinicId && r.CreatedAt >= since)
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .ToListAsync();

        foreach (var r in results)
        {
            items.Add(new NotificationPayload(
                $"lab-res-{r.Id:N}",
                "activity",
                "Laboratory Result",
                $"Result #{r.ResultNo} — {r.PatientName}",
                "/Laboratory/Result",
                r.CreatedAt));
        }
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
