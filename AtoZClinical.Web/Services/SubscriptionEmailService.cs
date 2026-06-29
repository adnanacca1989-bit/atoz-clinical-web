using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Billing;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Services;

public sealed class SubscriptionEmailService
{
    private const int OnboardingWelcome = 1;
    private const int OnboardingDay3 = 2;
    private const int OnboardingDay7 = 4;

    private readonly ClinicalDbContext _db;
    private readonly IClinicalEmailSender _email;
    private readonly ClinicalAppUrls _urls;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<SubscriptionEmailService> _logger;

    public SubscriptionEmailService(
        ClinicalDbContext db,
        IClinicalEmailSender email,
        ClinicalAppUrls urls,
        UserManager<ApplicationUser> users,
        ILogger<SubscriptionEmailService> logger)
    {
        _db = db;
        _email = email;
        _urls = urls;
        _users = users;
        _logger = logger;
    }

    public async Task SendLifecycleEmailsAsync(CancellationToken ct = default)
    {
        if (!_email.IsConfigured) return;

        await SendOnboardingEmailsAsync(ct);
        await SendTrialRemindersAsync(ct);
    }

    private async Task<bool> TrySendAsync(
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken ct)
    {
        try
        {
            var result = await _email.SendAsync(recipient, subject, htmlBody, ct);
            if (!result.Success)
            {
                _logger.LogError(
                    "Subscription email failed to {Recipient} subject={Subject}: {Reason}",
                    recipient, subject, result.Message);
                return false;
            }

            _logger.LogInformation(
                "Subscription email sent to {Recipient} subject={Subject}",
                recipient, subject);
            return true;
        }
        catch (ClinicalEmailNotConfiguredException ex)
        {
            _logger.LogWarning(ex, "Subscription email skipped: SMTP not configured");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscription email failed to {Recipient} subject={Subject}", recipient, subject);
            return false;
        }
    }

    private async Task SendOnboardingEmailsAsync(CancellationToken ct)
    {
        var clinics = await _db.Clinics
            .Where(c => c.Status == Core.Enums.ClinicStatus.Active || c.Status == Core.Enums.ClinicStatus.Pending)
            .ToListAsync(ct);

        foreach (var clinic in clinics)
        {
            var ageDays = (DateTime.UtcNow.Date - clinic.CreatedAt.Date).Days;
            var recipient = await ResolveRecipientEmailAsync(clinic, ct);
            if (recipient is null) continue;

            if (ageDays >= 0 && (clinic.OnboardingEmailsSent & OnboardingWelcome) == 0)
            {
                var billingUrl = _urls.BuildPageUrl("Billing/Index");
                if (!await TrySendAsync(recipient,
                    "Welcome to A to Z Clinical",
                    $"""
                    <p>Hello,</p>
                    <p>Your clinic <strong>{clinic.Name}</strong> is ready. Start with patient registration, then explore billing and reports from the sidebar.</p>
                    <p><a href="{billingUrl}">View subscription &amp; billing</a></p>
                    """,
                    ct))
                    continue;
                clinic.OnboardingEmailsSent |= OnboardingWelcome;
            }
            else if (ageDays >= 3 && (clinic.OnboardingEmailsSent & OnboardingDay3) == 0)
            {
                if (!await TrySendAsync(recipient,
                    "Quick tips for your clinic workspace",
                    """
                    <p>Hello,</p>
                    <p>Tip: use <strong>Workflow</strong> to track patient visits, and <strong>Dashboard</strong> for daily totals.</p>
                    <p>Need help? Reply to this email or contact your vendor.</p>
                    """,
                    ct))
                    continue;
                clinic.OnboardingEmailsSent |= OnboardingDay3;
            }
            else if (ageDays >= 7 && (clinic.OnboardingEmailsSent & OnboardingDay7) == 0 &&
                     clinic.PlanName.Equals(SubscriptionPlans.Trial, StringComparison.OrdinalIgnoreCase))
            {
                var upgradeUrl = _urls.BuildPageUrl("Billing/Index");
                if (!await TrySendAsync(recipient,
                    "Your trial is halfway through",
                    $"""
                    <p>Hello,</p>
                    <p>Your trial for <strong>{clinic.Name}</strong> is in progress. Upgrade anytime to keep uninterrupted access.</p>
                    <p><a href="{upgradeUrl}">Upgrade now</a></p>
                    """,
                    ct))
                    continue;
                clinic.OnboardingEmailsSent |= OnboardingDay7;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task SendTrialRemindersAsync(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var clinics = await _db.Clinics
            .Where(c => c.PlanName == SubscriptionPlans.Trial)
            .Where(c => c.TrialEndsAt != null && c.TrialEndsAt >= today)
            .ToListAsync(ct);

        foreach (var clinic in clinics)
        {
            var daysLeft = (clinic.TrialEndsAt!.Value.Date - today).Days;
            if (daysLeft is not (7 or 3 or 1 or 0)) continue;

            if (clinic.LastTrialReminderSentAt?.Date == today) continue;

            var recipient = await ResolveRecipientEmailAsync(clinic, ct);
            if (recipient is null) continue;

            var upgradeUrl = _urls.BuildPageUrl("Billing/Index");
            var subject = daysLeft switch
            {
                0 => "Your A to Z Clinical trial ends today",
                1 => "Your trial ends tomorrow",
                _ => $"Your trial ends in {daysLeft} days"
            };

            if (!await TrySendAsync(recipient, subject,
                $"""
                <p>Hello,</p>
                <p>Your trial for <strong>{clinic.Name}</strong> ends on <strong>{clinic.TrialEndsAt.Value:d}</strong>.</p>
                <p><a href="{upgradeUrl}">Upgrade your subscription</a> to keep your data and users active.</p>
                """,
                ct))
                continue;

            clinic.LastTrialReminderSentAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<string?> ResolveRecipientEmailAsync(Clinic clinic, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(clinic.Email))
            return clinic.Email.Trim();

        var admin = await _users.Users
            .Where(u => u.ClinicId == clinic.Id && u.ClinicRole == ClinicUserRole.ClinicAdmin)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(admin) ? null : admin.Trim();
    }
}
