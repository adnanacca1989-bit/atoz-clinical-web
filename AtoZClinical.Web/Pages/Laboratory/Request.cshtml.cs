using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Laboratory;

public class RequestModel : ClinicFormPageModel
{
    private readonly LabRequestService _service;
    private readonly LabTestService _labTests;
    private const int DefaultLineCount = 6;

    public RequestModel(ClinicContextService clinicContext, LabRequestService service, LabTestService labTests) : base(clinicContext)
    {
        _service = service;
        _labTests = labTests;
    }

    [BindProperty]
    public LabRequestInput Input { get; set; } = new();

    [BindProperty]
    public List<LabRequestLineInput> Lines { get; set; } = [];

    public List<LabRequest> Records { get; private set; } = [];
    public List<LabTest> RegisteredTests { get; private set; } = [];

    public decimal LineTotal => Lines.Sum(l => l.Qty * l.Fee);

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        RegisteredTests = await _labTests.ListAsync(clinicId.Value);
        if (ShouldLoadExistingRecord())
            await LoadRecord(clinicId.Value, RecordId!.Value);
        else
            await PrepareNew(clinicId.Value);
        ViewData["OpenPatientSelect"] = true;
        ViewData["ShowAddLines"] = true;
        SetFormViewData("Laboratory Request", null, null, Input.UpdatedAt);
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
                (r.PatientName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                (r.DoctorName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                r.RequestNo.ToString().Contains(Search)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = LabRequestInput.FromEntity(item);
        Input.Specialty = await ResolveDoctorSpecialtyAsync(clinicId, Input.DoctorName, Input.Specialty);
        Lines = item.Lines.OrderBy(l => l.LineNo).Select(LabRequestLineInput.FromEntity).ToList();
        EnsureLineRows();
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _service.ListAsync(clinicId);
        var next = (all.Count > 0 ? all.Max(r => r.RequestNo) : 0) + 1;
        Input = new LabRequestInput { RequestNo = next, RequestDate = DateTime.Today, Gender = "Male" };
        Lines = CreateEmptyLines();
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();

        if (!ModelState.IsValid)
        {
            await LoadAsync(clinicId.Value);
            RegisteredTests = await _labTests.ListAsync(clinicId.Value);
            ViewData["OpenPatientSelect"] = true;
            SetFormViewData("Laboratory Request", null, null, Input.UpdatedAt);
            EnsureLineRows();
            return Page();
        }

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
            Lines.Add(new LabRequestLineInput { LineNo = Lines.Count + 1 });
    }

    private static List<LabRequestLineInput> CreateEmptyLines() =>
        Enumerable.Range(1, DefaultLineCount).Select(i => new LabRequestLineInput { LineNo = i }).ToList();

    public sealed class LabRequestInput
    {
        public int RequestNo { get; set; }
        public DateTime RequestDate { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "Patient Name is required.")]
        [Display(Name = "Patient Name")]
        public string? PatientName { get; set; }

        public string? PatientBarcode { get; set; }

        [Required(ErrorMessage = "Age is required.")]
        [Range(0, 150, ErrorMessage = "Age is required.")]
        public int? Age { get; set; }

        [Required(ErrorMessage = "Gender is required.")]
        public string? Gender { get; set; }

        [Required(ErrorMessage = "Phone is required.")]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "City is required.")]
        public string? City { get; set; }

        [Required(ErrorMessage = "Doctor Name is required.")]
        [Display(Name = "Doctor Name")]
        public string? DoctorName { get; set; }

        [Required(ErrorMessage = "Specialty is required.")]
        public string? Specialty { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public static LabRequestInput FromEntity(LabRequest r) => new()
        {
            RequestNo = r.RequestNo,
            RequestDate = r.RequestDate,
            PatientName = r.PatientName,
            PatientBarcode = r.PatientBarcode,
            Age = r.Age,
            Gender = r.Gender,
            Phone = r.Phone,
            City = r.City,
            DoctorName = r.DoctorName,
            Specialty = r.Specialty,
            UpdatedAt = r.UpdatedAt
        };

        public LabRequest ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            RequestNo = RequestNo,
            RequestDate = RequestDate,
            PatientName = PatientName,
            PatientBarcode = PatientBarcode,
            Age = Age,
            Gender = Gender,
            Phone = Phone,
            City = City,
            DoctorName = DoctorName,
            Specialty = Specialty
        };
    }

    public sealed class LabRequestLineInput
    {
        public int LineNo { get; set; }
        public string? TestCode { get; set; }
        public string? TestName { get; set; }
        public string? Category { get; set; }
        public int Qty { get; set; } = 1;
        public decimal Fee { get; set; }

        public decimal Total => Qty * Fee;

        public static LabRequestLineInput FromEntity(LabRequestLine l) => new()
        {
            LineNo = l.LineNo,
            TestCode = l.TestCode,
            TestName = l.TestName,
            Category = l.Category,
            Qty = l.Qty,
            Fee = l.Fee
        };

        public LabRequestLine ToEntity() => new()
        {
            LineNo = LineNo,
            TestCode = TestCode,
            TestName = TestName,
            Category = Category,
            Qty = Qty,
            Fee = Fee,
            Total = Qty * Fee
        };
    }
}
