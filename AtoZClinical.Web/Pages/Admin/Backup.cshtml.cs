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

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Clinic" : name.Trim();
    }
}
