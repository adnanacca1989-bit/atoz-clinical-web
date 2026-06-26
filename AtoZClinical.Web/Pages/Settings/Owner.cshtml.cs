using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Settings;

public class OwnerModel : SettingsFormPageModel
{
    private readonly ClinicLookupService _lookup;

    public OwnerModel(ClinicContextService clinicContext, ClinicLookupService lookup) : base(clinicContext)
        => _lookup = lookup;

    [BindProperty] public OwnerInput Input { get; set; } = new();
    public List<ClinicOwner> Records { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireSettingsClinicIdAsync();
        if (clinicId is null) return ClinicRequired();
        await LoadAsync(clinicId.Value);
        if (ShouldLoadExistingRecord()) await LoadRecord(clinicId.Value, RecordId!.Value);
        else if (NewRecord) await PrepareNew(clinicId.Value);
        else if (Records.Count > 0) await LoadRecord(clinicId.Value, Records[0].Id);
        else await PrepareNew(clinicId.Value);
        SetFormViewData("Define Owner", null, null, Input.UpdatedAt);
        return Page();
    }

    protected override Task<IActionResult> SaveSettingsCoreAsync() => SaveCoreAsync();
    public Task<IActionResult> OnPostNewAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostClearAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostDeleteAsync() => DeleteCoreAsync();
    public Task<IActionResult> OnPostBackAsync() => NavigateCoreAsync(-1);
    public Task<IActionResult> OnPostNextAsync() => NavigateCoreAsync(1);

    private async Task LoadAsync(Guid clinicId)
    {
        Records = await _lookup.ListOwnersAsync(clinicId);
        if (!string.IsNullOrWhiteSpace(Search))
            Records = Records.Where(r =>
                r.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                (r.Email?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _lookup.GetOwnerAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = OwnerInput.FromEntity(item);
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _lookup.ListOwnersAsync(clinicId);
        Input = new OwnerInput { OwnerNo = (all.Count > 0 ? all.Max(x => x.OwnerNo) : 0) + 1, IsActive = true };
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireSettingsClinicIdAsync();
        if (clinicId is null) return ClinicRequired();
        ResolveRecordIdForSave();
        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            ModelState.AddModelError(string.Empty, "Owner name is required.");
            await LoadAsync(clinicId.Value);
            return Page();
        }
        var saved = await _lookup.SaveOwnerAsync(clinicId.Value, Input.ToEntity(RecordIdForSave), UserName);
        return RedirectAfterSave(saved.Id);
    }

    private Task<IActionResult> NewCoreAsync() => Task.FromResult<IActionResult>(RedirectToNewForm());

    private async Task<IActionResult> DeleteCoreAsync()
    {
        var clinicId = await RequireSettingsClinicIdAsync();
        if (clinicId is null || !RecordId.HasValue) return RedirectToPage();
        await _lookup.DeleteOwnerAsync(clinicId.Value, RecordId.Value, UserName);
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

    public sealed class OwnerInput
    {
        public int OwnerNo { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? UpdatedAt { get; set; }

        public static OwnerInput FromEntity(ClinicOwner e) => new()
        {
            OwnerNo = e.OwnerNo, Name = e.Name, Phone = e.Phone, Email = e.Email,
            Notes = e.Notes, IsActive = e.IsActive, UpdatedAt = e.UpdatedAt
        };

        public ClinicOwner ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty, OwnerNo = OwnerNo, Name = Name, Phone = Phone,
            Email = Email, Notes = Notes, IsActive = IsActive
        };
    }
}
