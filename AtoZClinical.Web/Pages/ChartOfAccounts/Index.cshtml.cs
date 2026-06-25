using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.ChartOfAccounts;

public class IndexModel : ClinicFormPageModel
{
    private readonly ChartAccountService _service;

    public IndexModel(ClinicContextService clinicContext, ChartAccountService service) : base(clinicContext)
    {
        _service = service;
    }

    [BindProperty]
    public ChartAccountInput Input { get; set; } = new();

    public List<ChartAccount> Records { get; private set; } = [];

    public IReadOnlyList<string> DetailTypeOptions =>
        ClinicLookup.GetDetailTypesForCategory(Input.CategoryType);

    public string DetailTypesByCategoryJson { get; private set; } = "{}";

    public async Task<IActionResult> OnGetAsync()
    {
        DetailTypesByCategoryJson = System.Text.Json.JsonSerializer.Serialize(ClinicLookup.AccountDetailTypesByCategory);
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        if (RecordId.HasValue)
            await LoadRecord(clinicId.Value, RecordId.Value);
        else if (NewRecord)
            await PrepareNew(clinicId.Value);
        else if (Records.Count > 0)
            await LoadRecord(clinicId.Value, Records[0].Id);
        else
            await PrepareNew(clinicId.Value);
        SetFormViewData("Chart of Accounts", null, null, Input.UpdatedAt);
        return Page();
    }

    protected override Task<IActionResult> ExecuteSaveAsync() => SaveCoreAsync();
    public Task<IActionResult> OnPostNewAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostClearAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostDeleteAsync() => DeleteCoreAsync();
    public Task<IActionResult> OnPostBackAsync() => NavigateCoreAsync(-1);
    public Task<IActionResult> OnPostNextAsync() => NavigateCoreAsync(1);

    private async Task LoadAsync(Guid clinicId)
    {
        Records = await _service.ListAsync(clinicId);
        if (!string.IsNullOrWhiteSpace(Search))
            Records = Records.Where(r =>
                r.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                r.CategoryType.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                r.DetailType.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = ChartAccountInput.FromEntity(item);
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var next = await _service.NextAccountNoAsync(clinicId);
        Input = new ChartAccountInput
        {
            AccountNo = next,
            CategoryType = ClinicLookup.AccountCategoryTypes[3]
        };
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            ModelState.AddModelError("Input.Name", "Name is required.");
            DetailTypesByCategoryJson = System.Text.Json.JsonSerializer.Serialize(ClinicLookup.AccountDetailTypesByCategory);
            await LoadAsync(clinicId.Value);
            return Page();
        }
        if (string.IsNullOrWhiteSpace(Input.DetailType))
        {
            ModelState.AddModelError("Input.DetailType", "Detail Type is required.");
            DetailTypesByCategoryJson = System.Text.Json.JsonSerializer.Serialize(ClinicLookup.AccountDetailTypesByCategory);
            await LoadAsync(clinicId.Value);
            return Page();
        }
        var allowedDetailTypes = ClinicLookup.GetDetailTypesForCategory(Input.CategoryType);
        if (allowedDetailTypes.Length > 0 &&
            !allowedDetailTypes.Contains(Input.DetailType, StringComparer.OrdinalIgnoreCase) &&
            !RecordId.HasValue)
        {
            ModelState.AddModelError("Input.DetailType", "Detail Type does not match the selected Category Type.");
            DetailTypesByCategoryJson = System.Text.Json.JsonSerializer.Serialize(ClinicLookup.AccountDetailTypesByCategory);
            await LoadAsync(clinicId.Value);
            return Page();
        }
        var isNew = string.Equals(SaveMode, "New", StringComparison.OrdinalIgnoreCase) || !RecordId.HasValue;
        var entity = Input.ToEntity(isNew ? null : RecordId);
        var saved = await _service.SaveAsync(clinicId.Value, entity, UserName);
        return RedirectAfterSave(saved.Id);
    }

    private Task<IActionResult> NewCoreAsync() =>
        Task.FromResult<IActionResult>(RedirectToNewForm());

    private async Task<IActionResult> DeleteCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null || !RecordId.HasValue) return RedirectToPage();
        await _service.DeleteAsync(clinicId.Value, RecordId.Value, UserName);
        return RedirectToPage();
    }

    private async Task<IActionResult> NavigateCoreAsync(int delta)
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        if (Records.Count == 0) return RedirectToPage();
        var idx = RecordId.HasValue ? Records.FindIndex(r => r.Id == RecordId.Value) : 0;
        if (idx < 0) idx = 0;
        idx = Math.Clamp(idx + delta, 0, Records.Count - 1);
        return RedirectToRecord(Records[idx].Id);
    }

    public sealed class ChartAccountInput
    {
        public int AccountNo { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CategoryType { get; set; } = "Income";
        public string DetailType { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static ChartAccountInput FromEntity(ChartAccount a) => new()
        {
            AccountNo = a.AccountNo,
            Name = a.Name,
            CategoryType = a.CategoryType,
            DetailType = a.DetailType,
            Description = a.Description,
            UpdatedAt = a.UpdatedAt
        };

        public ChartAccount ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            AccountNo = AccountNo,
            Name = Name.Trim(),
            CategoryType = CategoryType,
            DetailType = DetailType,
            Description = Description
        };
    }
}
