using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Radiology;

public class ResultModel : ClinicFormPageModel
{
    private readonly RadiologyResultService _service;
    private const int DefaultLineCount = 6;

    public ResultModel(ClinicContextService clinicContext, RadiologyResultService service) : base(clinicContext)
    {
        _service = service;
    }

    [BindProperty]
    public RadiologyResultInput Input { get; set; } = new();

    [BindProperty]
    public List<RadiologyResultLineInput> Lines { get; set; } = [];

    public List<RadiologyResult> Records { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        if (RecordId.HasValue)
            await LoadRecord(clinicId.Value, RecordId.Value);
        else if (NewRecord)
            await PrepareNew(clinicId.Value);
        else if (Records.Count > 0 && Input.ResultNo == 0)
            await LoadRecord(clinicId.Value, Records[0].Id);
        else
            await PrepareNew(clinicId.Value);
        ViewData["OpenPatientSelect"] = true;
        ViewData["ShowAddLines"] = true;
        SetFormViewData("Radiology Result", null, null, Input.UpdatedAt);
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
                (r.PatientName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                (r.DoctorName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                r.ResultNo.ToString().Contains(Search) ||
                (r.RequestNo?.ToString().Contains(Search) == true)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = RadiologyResultInput.FromEntity(item);
        Lines = item.Lines.OrderBy(l => l.LineNo).Select(RadiologyResultLineInput.FromEntity).ToList();
        EnsureLineRows();
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _service.ListAsync(clinicId);
        var next = (all.Count > 0 ? all.Max(r => r.ResultNo) : 0) + 1;
        Input = new RadiologyResultInput { ResultNo = next, ResultDate = DateTime.Today };
        Lines = CreateEmptyLines();
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        var entity = Input.ToEntity(RecordId);
        var lines = Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.TestCode) || !string.IsNullOrWhiteSpace(l.TestName))
            .Select(l => l.ToEntity())
            .ToList();
        var saved = await _service.SaveAsync(clinicId.Value, entity, lines, UserName);
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
        return await SafeDeleteAsync(
            () => _service.DeleteAsync(clinicId.Value, RecordId.Value, UserName),
            async () =>
            {
                await LoadAsync(clinicId.Value);
                if (RecordId.HasValue) await LoadRecord(clinicId.Value, RecordId.Value);
            });
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

    private void EnsureLineRows()
    {
        while (Lines.Count < DefaultLineCount)
            Lines.Add(new RadiologyResultLineInput { LineNo = Lines.Count + 1 });
    }

    private static List<RadiologyResultLineInput> CreateEmptyLines() =>
        Enumerable.Range(1, DefaultLineCount).Select(i => new RadiologyResultLineInput { LineNo = i }).ToList();

    public sealed class RadiologyResultInput
    {
        public int ResultNo { get; set; }
        public int? RequestNo { get; set; }
        public DateTime ResultDate { get; set; } = DateTime.Today;
        public string? PatientName { get; set; }
        public string? PatientBarcode { get; set; }
        public int? Age { get; set; }
        public string? Gender { get; set; }
        public string? Phone { get; set; }
        public string? City { get; set; }
        public string? DoctorName { get; set; }
        public string? Specialty { get; set; }
        public string? Notes { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static RadiologyResultInput FromEntity(RadiologyResult r) => new()
        {
            ResultNo = r.ResultNo,
            RequestNo = r.RequestNo,
            ResultDate = r.ResultDate,
            PatientName = r.PatientName,
            DoctorName = r.DoctorName,
            Specialty = r.Specialty,
            Notes = r.Notes,
            UpdatedAt = r.UpdatedAt
        };

        public RadiologyResult ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            ResultNo = ResultNo,
            RequestNo = RequestNo,
            ResultDate = ResultDate,
            PatientName = PatientName,
            DoctorName = DoctorName,
            Specialty = Specialty,
            Notes = Notes
        };
    }

    public sealed class RadiologyResultLineInput
    {
        public int LineNo { get; set; }
        public string? TestCode { get; set; }
        public string? TestName { get; set; }
        public string? Category { get; set; }
        public string? Result { get; set; }
        public string? NormalRange { get; set; }
        public string? Unit { get; set; }

        public static RadiologyResultLineInput FromEntity(RadiologyResultLine l) => new()
        {
            LineNo = l.LineNo,
            TestCode = l.TestCode,
            TestName = l.TestName,
            Category = l.Category,
            Result = l.Result,
            NormalRange = l.Impression,
            Unit = l.Findings
        };

        public RadiologyResultLine ToEntity() => new()
        {
            LineNo = LineNo,
            TestCode = TestCode,
            TestName = TestName,
            Category = Category,
            Result = Result,
            Impression = NormalRange,
            Findings = Unit
        };
    }
}
