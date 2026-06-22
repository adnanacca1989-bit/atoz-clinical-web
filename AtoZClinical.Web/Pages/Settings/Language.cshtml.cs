using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Settings;

public class LanguageModel : SettingsFormPageModel
{
    private readonly ClinicLookupService _lookup;

    public LanguageModel(ClinicContextService clinicContext, ClinicLookupService lookup) : base(clinicContext)
        => _lookup = lookup;

    [BindProperty] public LanguageInput Input { get; set; } = new();
    public List<ClinicLanguage> Records { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireSettingsClinicIdAsync();
        if (clinicId is null) return ClinicRequired();
        await LoadAsync(clinicId.Value);
        if (RecordId.HasValue) await LoadRecord(clinicId.Value, RecordId.Value);
        else if (NewRecord) await PrepareNew(clinicId.Value);
        else if (Records.Count > 0) await LoadRecord(clinicId.Value, Records[0].Id);
        else await PrepareNew(clinicId.Value);
        SetFormViewData("Define Language", null, null, Input.UpdatedAt);
        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync();
    public Task<IActionResult> OnPostNewAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostClearAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostDeleteAsync() => DeleteCoreAsync();
    public Task<IActionResult> OnPostBackAsync() => NavigateCoreAsync(-1);
    public Task<IActionResult> OnPostNextAsync() => NavigateCoreAsync(1);

    private async Task LoadAsync(Guid clinicId)
    {
        Records = await _lookup.ListLanguagesAsync(clinicId);
        if (!string.IsNullOrWhiteSpace(Search))
            Records = Records.Where(r =>
                r.Code.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                r.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _lookup.GetLanguageAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = LanguageInput.FromEntity(item);
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _lookup.ListLanguagesAsync(clinicId);
        Input = new LanguageInput { LanguageNo = (all.Count > 0 ? all.Max(x => x.LanguageNo) : 0) + 1, IsActive = true };
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireSettingsClinicIdAsync();
        if (clinicId is null) return ClinicRequired();
        if (string.IsNullOrWhiteSpace(Input.Code))
        {
            ModelState.AddModelError(string.Empty, "Language code is required.");
            await LoadAsync(clinicId.Value);
            return Page();
        }
        var saved = await _lookup.SaveLanguageAsync(clinicId.Value, Input.ToEntity(RecordId), UserName);
        return RedirectAfterSave(saved.Id);
    }

    private Task<IActionResult> NewCoreAsync() => Task.FromResult<IActionResult>(RedirectToNewForm());

    private async Task<IActionResult> DeleteCoreAsync()
    {
        var clinicId = await RequireSettingsClinicIdAsync();
        if (clinicId is null || !RecordId.HasValue) return RedirectToPage();
        await _lookup.DeleteLanguageAsync(clinicId.Value, RecordId.Value, UserName);
        return RedirectToPage();
    }

    private async Task<IActionResult> NavigateCoreAsync(int delta)
    {
        var clinicId = await RequireSettingsClinicIdAsync();
        if (clinicId is null) return ClinicRequired();
        await LoadAsync(clinicId.Value);
        if (Records.Count == 0) return RedirectToPage();
        var idx = RecordId.HasValue ? Records.FindIndex(r => r.Id == RecordId.Value) : 0;
        if (idx < 0) idx = 0;
        idx = Math.Clamp(idx + delta, 0, Records.Count - 1);
        return RedirectToRecord(Records[idx].Id);
    }

    public sealed class LanguageInput
    {
        public int LanguageNo { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? UpdatedAt { get; set; }

        public static LanguageInput FromEntity(ClinicLanguage e) => new()
        {
            LanguageNo = e.LanguageNo, Code = e.Code, Name = e.Name,
            IsDefault = e.IsDefault, IsActive = e.IsActive, UpdatedAt = e.UpdatedAt
        };

        public ClinicLanguage ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty, LanguageNo = LanguageNo, Code = Code, Name = Name,
            IsDefault = IsDefault, IsActive = IsActive
        };
    }
}
