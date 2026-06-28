using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Settings;

public class UnlinkedDoctorUsersModel : SettingsPageModel
{
    private readonly DoctorUserLinkService _doctorUserLinks;
    private readonly ClinicalDbContext _db;

    public UnlinkedDoctorUsersModel(
        ClinicContextService clinicContext,
        ClinicSettingsService settingsService,
        DoctorUserLinkService doctorUserLinks,
        ClinicalDbContext db)
        : base(clinicContext, settingsService)
    {
        _doctorUserLinks = doctorUserLinks;
        _db = db;
    }

    public List<DoctorUserLinkService.UnlinkedDoctorUserRow> UnlinkedUsers { get; private set; } = [];
    public int PatientsMissingDoctorLink { get; private set; }
    public string? StatusMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadClinicContextAsync()) return Page();
        if (!await RequireAdminAsync()) return Forbid();
        await LoadReportAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAutoLinkAsync()
    {
        if (!await LoadClinicContextAsync()) return Page();
        if (!await RequireAdminAsync()) return Forbid();

        var clinicId = await ClinicContext.GetClinicIdAsync();
        if (clinicId is null) return Page();

        var linked = await _doctorUserLinks.BackfillClinicAsync(clinicId.Value);
        StatusMessage = linked > 0
            ? $"Auto-linked {linked} doctor user(s) by matching name."
            : "No doctor users could be auto-linked. Edit each user in Define User and select a doctor.";

        await LoadReportAsync();
        return Page();
    }

    private async Task LoadReportAsync()
    {
        var clinicId = await ClinicContext.GetClinicIdAsync();
        if (clinicId is null) return;

        UnlinkedUsers = await _doctorUserLinks.ListUnlinkedAsync(clinicId.Value);
        PatientsMissingDoctorLink = await _db.Patients
            .IgnoreQueryFilters()
            .CountAsync(p => p.ClinicId == clinicId && p.DoctorRecordId == null);
    }

    private async Task<bool> RequireAdminAsync()
    {
        var user = await ClinicContext.GetCurrentUserAsync();
        return user is { IsVendorAdmin: true } or { ClinicRole: ClinicUserRole.ClinicAdmin };
    }
}
