using AtoZClinical.Infrastructure.Data;
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

    private static readonly string[] WatchedForms =
    [
        "Cash Receipt", "Cash Payment", "Laboratory Request", "Laboratory Result",
        "Radiology Request", "Radiology Result", "Pharmacy Request", "Pharmacy Bill",
        "Invoice", "Patient Registration", "Prescription"
    ];

    public FeedModel(ClinicContextService clinicContext, ClinicalDbContext db, AppointmentReminderService reminders)
    {
        _clinicContext = clinicContext;
        _db = db;
        _reminders = reminders;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var apptCount = await _reminders.GetUpcomingReminderCountAsync(clinicId.Value);

        var since = DateTime.UtcNow.AddHours(-24);
        var entries = await _db.AuditLogEntries
            .AsNoTracking()
            .Where(a => a.ClinicId == clinicId && a.DateTime >= since)
            .Where(a => a.Type == "Create" || a.Type == "Update")
            .OrderByDescending(a => a.DateTime)
            .Take(40)
            .ToListAsync();

        var items = entries
            .Where(e => WatchedForms.Any(f => (e.FormName ?? "").Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Take(15)
            .Select(e => new
            {
                kind = "activity",
                title = $"{e.FormName} {e.Type}",
                detail = e.Details ?? "",
                at = e.DateTime.ToLocalTime().ToString("g")
            })
            .ToList<object>();

        if (apptCount > 0)
        {
            items.Insert(0, new
            {
                kind = "appointment",
                title = "Appointment Reminder",
                detail = apptCount == 1
                    ? "1 appointment is within 15 minutes."
                    : $"{apptCount} appointments are within 15 minutes.",
                at = DateTime.Now.ToString("g")
            });
        }

        return new JsonResult(new { appointmentCount = apptCount, items });
    }
}
