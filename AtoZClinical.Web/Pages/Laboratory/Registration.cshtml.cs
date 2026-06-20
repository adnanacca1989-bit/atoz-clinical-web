using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Laboratory;

public class RegistrationModel : ClinicFormPageModel
{
    private readonly LabTestService _service;

    public RegistrationModel(ClinicContextService clinicContext, LabTestService service) : base(clinicContext)
    {
        _service = service;
    }

    [BindProperty]
    public LabTestInput Input { get; set; } = new();

    public List<LabTest> Records { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        if (RecordId.HasValue)
            await LoadRecord(clinicId.Value, RecordId.Value);
        else if (NewRecord)
            await PrepareNew(clinicId.Value);
        else if (Records.Count > 0 && Input.TestNo == 0)
            await LoadRecord(clinicId.Value, Records[0].Id);
        else
            await PrepareNew(clinicId.Value);
        SetFormViewData("Laboratory Registration", null, null, Input.UpdatedAt);
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
        Records = await _service.ListAsync(clinicId);
        if (!string.IsNullOrWhiteSpace(Search))
            Records = Records.Where(r =>
                r.TestCode.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                r.TestName.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                r.Category.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = LabTestInput.FromEntity(item);
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _service.ListAsync(clinicId);
        var next = (all.Count > 0 ? all.Max(t => t.TestNo) : 0) + 1;
        Input = new LabTestInput { TestNo = next, Category = ClinicLookup.LabCategories[0] };
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();

        if (string.IsNullOrWhiteSpace(Input?.TestCode) || string.IsNullOrWhiteSpace(Input?.TestName))
        {
            ModelState.AddModelError(string.Empty, "Test Code and Test Name are required.");
            await LoadAsync(clinicId.Value);
            SetFormViewData("Laboratory Registration", null, null, Input?.UpdatedAt);
            return Page();
        }

        var entity = Input.ToEntity(RecordId);
        var saved = await _service.SaveAsync(clinicId.Value, entity);
        return RedirectAfterSave(saved.Id);
    }

    private Task<IActionResult> NewCoreAsync()
    {
        RecordId = null;
        return Task.FromResult<IActionResult>(RedirectToNewForm());
    }

    private async Task<IActionResult> DeleteCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null || !RecordId.HasValue) return RedirectToPage();
        await _service.DeleteAsync(clinicId.Value, RecordId.Value);
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

    public sealed class LabTestInput
    {
        public int TestNo { get; set; }
        public string TestCode { get; set; } = string.Empty;
        public string TestName { get; set; } = string.Empty;
        public string Category { get; set; } = "Hematology";
        public decimal Fee { get; set; }
        public string? Note { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static LabTestInput FromEntity(LabTest t) => new()
        {
            TestNo = t.TestNo,
            TestCode = t.TestCode,
            TestName = t.TestName,
            Category = t.Category,
            Fee = t.Fee,
            Note = t.Note,
            UpdatedAt = t.UpdatedAt
        };

        public LabTest ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            TestNo = TestNo,
            TestCode = (TestCode ?? string.Empty).Trim(),
            TestName = (TestName ?? string.Empty).Trim(),
            Category = Category ?? ClinicLookup.LabCategories[0],
            Fee = Fee,
            Note = Note
        };
    }
}
