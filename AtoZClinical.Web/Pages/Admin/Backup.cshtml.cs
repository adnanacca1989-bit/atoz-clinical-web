using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Admin;

public class BackupModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly ClinicBackupService _backup;
    private readonly AuditService _audit;

    public BackupModel(ClinicContextService clinicContext, ClinicBackupService backup, AuditService audit)
    {
        _clinicContext = clinicContext;
        _backup = backup;
        _audit = audit;
    }

    public string ClinicName { get; private set; } = string.Empty;
    public string StatusText => $"{DateTime.Now:M/d/yyyy h:mm tt} - Ready";
    public RestoreSummary? LastRestore { get; private set; }
    public string? RestoreMessage { get; private set; }

    [BindProperty]
    public IFormFile? RestoreFile { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var clinic = await _clinicContext.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();
        ClinicName = clinic.Name;
        return Page();
    }

    public async Task<IActionResult> OnPostExportZipAsync()
    {
        var clinic = await _clinicContext.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();

        var bytes = await _backup.ExportExcelFilesZipAsync(clinic.Id, clinic.Name);
        var safeName = SanitizeFileName(clinic.Name);
        await _audit.LogAsync(clinic.Id, User.Identity?.Name, "Data Backup", "Export",
            $"Excel files ZIP backup for {clinic.Name}");

        return File(bytes, "application/zip", $"{safeName}_Backup_{DateTime.Now:yyyyMMdd_HHmm}.zip");
    }

    public async Task<IActionResult> OnPostExportWorkbookAsync()
    {
        var clinic = await _clinicContext.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();

        var bytes = await _backup.ExportWorkbookAsync(clinic.Id);
        var safeName = SanitizeFileName(clinic.Name);
        await _audit.LogAsync(clinic.Id, User.Identity?.Name, "Data Backup", "Export",
            $"Excel workbook backup for {clinic.Name}");

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{safeName}_Backup_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    public async Task<IActionResult> OnPostRestoreAsync()
    {
        var clinic = await _clinicContext.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();
        ClinicName = clinic.Name;

        if (RestoreFile is null || RestoreFile.Length == 0)
        {
            RestoreMessage = "Please select a backup ZIP file to restore.";
            return Page();
        }

        try
        {
            await using var stream = RestoreFile.OpenReadStream();
            LastRestore = await _backup.RestoreFromZipAsync(clinic.Id, stream);
            RestoreMessage =
                $"Restore complete: {LastRestore.PatientsImported} patients, {LastRestore.DoctorsImported} doctors, {LastRestore.ChartAccountsImported} chart accounts imported.";
            await _audit.LogAsync(clinic.Id, User.Identity?.Name, "Data Backup", "Restore", RestoreMessage);
        }
        catch (Exception ex)
        {
            RestoreMessage = $"Restore failed: {ex.Message}";
        }

        return Page();
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Clinic" : name.Trim();
    }
}
