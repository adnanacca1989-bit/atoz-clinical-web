using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Web.Pages;

public abstract class ClinicFormPageModel : PageModel
{
    protected readonly ClinicContextService ClinicContext;

    protected ClinicFormPageModel(ClinicContextService clinicContext) => ClinicContext = clinicContext;

    [BindProperty(SupportsGet = true)]
    public Guid? RecordId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool NewRecord { get; set; }

    [BindProperty(SupportsGet = true)]
    public int RecordPage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = PaginationDefaults.DefaultPageSize;

    public int TotalRecords { get; protected set; }
    public int TotalPages { get; protected set; }

    [BindProperty]
    public string SaveMode { get; set; } = "Edit";

    public string UserName => User.Identity?.Name ?? "admin";
    public string StatusText => $"{DateTime.Now:M/d/yyyy h:mm tt} - Ready";

    protected async Task<Guid?> RequireClinicIdAsync()
    {
        if (await ClinicContext.IsVendorAsync())
            return null;

        var access = await ClinicContext.GetClinicAccessAsync();
        if (!access.IsAllowed)
            return null;

        return access.Clinic?.Id ?? await ClinicContext.GetClinicIdAsync();
    }

    protected void SetFormViewData(string title, string? createdBy, string? updatedBy, DateTime? updatedAt)
    {
        ViewData["FormTitle"] = title;
        ViewData["StatusText"] = StatusText;
        ViewData["RecordId"] = RecordId?.ToString() ?? "";
        ViewData["CreatedBy"] = createdBy ?? UserName;
        ViewData["UpdatedBy"] = updatedBy ?? UserName;
        ViewData["UpdatedOn"] = updatedAt?.ToString("M/d/yyyy h:mm tt") ?? DateTime.Now.ToString("M/d/yyyy h:mm tt");
    }

    protected IActionResult RedirectToRecord(Guid? id) =>
        RedirectToPage(new { RecordId = id, Search, RecordPage, PageSize });

    protected void ApplyPaging<T>(PagedResult<T> result)
    {
        TotalRecords = result.TotalCount;
        TotalPages = result.TotalPages;
        RecordPage = result.Page;
        PageSize = result.PageSize;
    }

    protected IActionResult RedirectToNewForm() =>
        RedirectToPage(new { Search, NewRecord = true });

    protected IActionResult RedirectAfterSave(Guid savedId) =>
        SaveMode == "New" ? RedirectToNewForm() : RedirectToRecord(savedId);

    protected void ConfigureAddSave()
    {
        SaveMode = "New";
        RecordId = null;
    }

    protected void ConfigureEditSave() => SaveMode = "Edit";

    protected bool IsNewSave =>
        string.Equals(SaveMode, "New", StringComparison.OrdinalIgnoreCase);

    /// <summary>Clears RecordId when Add/New save so query-string ids cannot force an update.</summary>
    protected void ResolveRecordIdForSave()
    {
        if (IsNewSave)
            RecordId = null;
    }

    protected Guid? RecordIdForSave => IsNewSave ? null : RecordId;

    protected virtual Task<IActionResult> ExecuteSaveAsync() =>
        throw new InvalidOperationException($"{GetType().Name} must override ExecuteSaveAsync.");

    public async Task<IActionResult> OnPostAddAsync()
    {
        ConfigureAddSave();
        return await RunSaveSafelyAsync(ExecuteSaveAsync);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        ConfigureEditSave();
        return await RunSaveSafelyAsync(ExecuteSaveAsync);
    }

    private async Task<IActionResult> RunSaveSafelyAsync(Func<Task<IActionResult>> save)
    {
        try
        {
            return await save();
        }
        catch (InvalidOperationException ex)
        {
            HttpContext.RequestServices.GetService<ILogger<ClinicFormPageModel>>()?
                .LogWarning(ex, "Save rejected on {Page}", GetType().Name);
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (DbUpdateException ex)
        {
            HttpContext.RequestServices.GetService<ILogger<ClinicFormPageModel>>()?
                .LogError(ex, "Database save error on {Page}", GetType().Name);
            ModelState.AddModelError(string.Empty,
                $"Could not save this record: {ClinicSaveHelper.DbMessage(ex)}");
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetService<ILogger<ClinicFormPageModel>>()?
                .LogError(ex, "Unhandled save error on {Page}", GetType().Name);
            ModelState.AddModelError(string.Empty,
                "Could not save this record. Please click + New, refresh the page, and try again.");
        }

        try
        {
            await ReloadAfterSaveFailureAsync();
        }
        catch (Exception reloadEx)
        {
            HttpContext.RequestServices.GetService<ILogger<ClinicFormPageModel>>()?
                .LogError(reloadEx, "Failed to reload form after save error on {Page}", GetType().Name);
        }

        return Page();
    }

    /// <summary>Override to reload lists/dropdowns when RunSaveSafelyAsync catches an unexpected error.</summary>
    protected virtual Task ReloadAfterSaveFailureAsync() => Task.CompletedTask;

    /// <summary>GET with ?handler=Save after a failed POST bookmark — redirect to a safe GET.</summary>
    public IActionResult OnGetSave() => RedirectToPage(new { RecordId, Search, NewRecord, RecordPage, PageSize });

    public IActionResult OnGetAdd() => RedirectToPage(new { Search, NewRecord = true, RecordPage, PageSize });

    public IActionResult OnGetNew() => RedirectToPage(new { Search, NewRecord = true, RecordPage, PageSize });

    public IActionResult OnGetClear() => RedirectToPage(new { Search, NewRecord = true, RecordPage, PageSize });

    public IActionResult OnGetDelete() => RedirectToPage(new { RecordId, Search, RecordPage, PageSize });

    public IActionResult OnGetBack() => RedirectToPage(new { RecordId, Search, RecordPage, PageSize });

    public IActionResult OnGetNext() => RedirectToPage(new { RecordId, Search, RecordPage, PageSize });

    protected async Task<IActionResult> SafeDeleteAsync(Func<Task> deleteAction, Func<Task>? reloadAsync = null)
    {
        try
        {
            await deleteAction();
            return RedirectToPage();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            if (reloadAsync is not null)
                await reloadAsync();
            return Page();
        }
    }
}
