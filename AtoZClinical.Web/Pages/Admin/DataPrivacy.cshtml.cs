using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Admin;

public class DataPrivacyModel : PageModel
{
    private readonly ClinicContextService _context;
    private readonly ClinicBackupService _backup;
    private readonly ClinicDataDeletionService _deletion;

    public DataPrivacyModel(
        ClinicContextService context,
        ClinicBackupService backup,
        ClinicDataDeletionService deletion)
    {
        _context = context;
        _backup = backup;
        _deletion = deletion;
    }

    [BindProperty]
    public string ConfirmClinicName { get; set; } = "";

    public string ClinicName { get; private set; } = "";
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var clinic = await _context.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();
        ClinicName = clinic.Name;
        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        var clinic = await _context.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();

        var bytes = await _backup.ExportExcelFilesZipAsync(clinic.Id, clinic.Name);
        var safeName = string.Join("_", clinic.Name.Split(Path.GetInvalidFileNameChars()));
        return File(bytes, "application/zip", $"{safeName}-gdpr-export-{DateTime.UtcNow:yyyyMMdd}.zip");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var clinic = await _context.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();

        ClinicName = clinic.Name;
        if (!string.Equals(ConfirmClinicName.Trim(), clinic.Name.Trim(), StringComparison.Ordinal))
        {
            ErrorMessage = "Clinic name confirmation did not match. Deletion was canceled.";
            return Page();
        }

        await _deletion.DeleteClinicAndAllDataAsync(clinic.Id);
        return RedirectToPage("/Account/Login");
    }
}
