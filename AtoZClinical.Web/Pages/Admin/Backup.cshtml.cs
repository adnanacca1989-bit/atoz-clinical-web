using AtoZClinical.Core.Entities;
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
    private readonly ClinicBackupHistoryService _history;

    public BackupModel(
        ClinicContextService clinicContext,
        ClinicBackupService backup,
        AuditService audit,
        ClinicBackupHistoryService history)
    {
        _clinicContext = clinicContext;
        _backup = backup;
        _audit = audit;
        _history = history;
    }

    public string ClinicName { get; private set; } = string.Empty;
    public string StatusText => $"{DateTime.Now:M/d/yyyy h:mm tt} - Ready";
    public RestoreSummary? LastRestore { get; private set; }
    public string? RestoreMessage { get; private set; }
    public List<ClinicBackupHistory> History { get; private set; } = [];

    [BindProperty]
    public IFormFile? RestoreFile { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var clinic = await _clinicContext.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();
        ClinicName = clinic.Name;
        History = await _history.ListAsync(clinic.Id);
        return Page();
    }

    public async Task<IActionResult> OnPostExportZipAsync()
    {
        var clinic = await _clinicContext.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();

        var bytes = await _backup.ExportExcelFilesZipAsync(clinic.Id, clinic.Name);
        var safeName = SanitizeFileName(clinic.Name);
        var fileName = $"{safeName}_Backup_{DateTime.Now:yyyyMMdd_HHmm}.zip";

        await _audit.LogAsync(clinic.Id, User.Identity?.Name, "Data Backup", "Export",
            $"Excel files ZIP backup for {clinic.Name}");
        await _history.RecordAsync(clinic.Id, "ExportZip", fileName, bytes.Length, User.Identity?.Name);

        return File(bytes, "application/zip", fileName);
    }

    public async Task<IActionResult> OnPostExportWorkbookAsync()
    {
        var clinic = await _clinicContext.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();

        var bytes = await _backup.ExportWorkbookAsync(clinic.Id);
        var safeName = SanitizeFileName(clinic.Name);
        var fileName = $"{safeName}_Backup_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

        await _audit.LogAsync(clinic.Id, User.Identity?.Name, "Data Backup", "Export",
            $"Excel workbook backup for {clinic.Name}");
        await _history.RecordAsync(clinic.Id, "ExportWorkbook", fileName, bytes.Length, User.Identity?.Name);

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private const long MaxRestoreBytes = 52_428_800;

    public async Task<IActionResult> OnPostRestoreAsync()
    {
        var clinic = await _clinicContext.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();
        ClinicName = clinic.Name;
        History = await _history.ListAsync(clinic.Id);

        if (RestoreFile is null || RestoreFile.Length == 0)
        {
            RestoreMessage = "Please select a backup ZIP file to restore.";
            return Page();
        }

        if (RestoreFile.Length > MaxRestoreBytes)
        {
            RestoreMessage = "Backup file is too large. Maximum allowed size is 50 MB.";
            return Page();
        }

        if (!string.Equals(Path.GetExtension(RestoreFile.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            RestoreMessage = "Only ZIP backup files are supported.";
            return Page();
        }

        try
        {
            await using var stream = RestoreFile.OpenReadStream();
            LastRestore = await _backup.RestoreFromZipAsync(clinic.Id, stream);
            RestoreMessage =
                $"Restore complete: {LastRestore.PatientsImported} patients, {LastRestore.DoctorsImported} doctors, {LastRestore.ChartAccountsImported} chart accounts imported.";
            await _audit.LogAsync(clinic.Id, User.Identity?.Name, "Data Backup", "Restore", RestoreMessage);
            await _history.RecordAsync(
                clinic.Id,
                "Restore",
                RestoreFile.FileName,
                RestoreFile.Length,
                User.Identity?.Name,
                RestoreMessage);
            History = await _history.ListAsync(clinic.Id);
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
