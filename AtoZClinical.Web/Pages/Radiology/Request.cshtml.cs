using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Radiology;

public class RequestModel : ClinicFormPageModel
{
    private readonly RadiologyRequestService _service;
    private readonly RadiologyTestService _radiologyTests;
    private const int DefaultLineCount = 6;

    public RequestModel(ClinicContextService clinicContext, RadiologyRequestService service, RadiologyTestService radiologyTests) : base(clinicContext)
    {
        _service = service;
        _radiologyTests = radiologyTests;
    }

    [BindProperty]
    public RadiologyRequestInput Input { get; set; } = new();

    [BindProperty]
    public List<RadiologyRequestLineInput> Lines { get; set; } = [];

    public List<RadiologyRequest> Records { get; private set; } = [];
    public List<RadiologyTest> RegisteredTests { get; private set; } = [];

    public decimal LineTotal => Lines.Sum(l => l.Qty * l.Fee);

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        RegisteredTests = await _radiologyTests.ListAsync(clinicId.Value);
        if (RecordId.HasValue)
            await LoadRecord(clinicId.Value, RecordId.Value);
        else if (NewRecord)
            await PrepareNew(clinicId.Value);
        else if (Records.Count > 0 && Input.RequestNo == 0)
            await LoadRecord(clinicId.Value, Records[0].Id);
        else
            await PrepareNew(clinicId.Value);
        ViewData["OpenPatientSelect"] = true;
        SetFormViewData("Radiology Request", null, null, Input.UpdatedAt);
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
                r.RequestNo.ToString().Contains(Search)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = RadiologyRequestInput.FromEntity(item);
        Lines = item.Lines.OrderBy(l => l.LineNo).Select(RadiologyRequestLineInput.FromEntity).ToList();
        EnsureLineRows();
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _service.ListAsync(clinicId);
        var next = (all.Count > 0 ? all.Max(r => r.RequestNo) : 0) + 1;
        Input = new RadiologyRequestInput { RequestNo = next, RequestDate = DateTime.Today, Gender = "Male" };
        Lines = CreateEmptyLines();
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();

        if (!ModelState.IsValid)
        {
            await LoadAsync(clinicId.Value);
            RegisteredTests = await _radiologyTests.ListAsync(clinicId.Value);
            ViewData["OpenPatientSelect"] = true;
            SetFormViewData("Radiology Request", null, null, Input.UpdatedAt);
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

    private void EnsureLineRows()
    {
        while (Lines.Count < DefaultLineCount)
            Lines.Add(new RadiologyRequestLineInput { LineNo = Lines.Count + 1 });
    }

    private static List<RadiologyRequestLineInput> CreateEmptyLines() =>
        Enumerable.Range(1, DefaultLineCount).Select(i => new RadiologyRequestLineInput { LineNo = i }).ToList();

    public sealed class RadiologyRequestInput
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

        public static RadiologyRequestInput FromEntity(RadiologyRequest r) => new()
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

        public RadiologyRequest ToEntity(Guid? id) => new()
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

    public sealed class RadiologyRequestLineInput
    {
        public int LineNo { get; set; }
        public string? TestCode { get; set; }
        public string? TestName { get; set; }
        public string? Category { get; set; }
        public int Qty { get; set; } = 1;
        public decimal Fee { get; set; }

        public decimal Total => Qty * Fee;

        public static RadiologyRequestLineInput FromEntity(RadiologyRequestLine l) => new()
        {
            LineNo = l.LineNo,
            TestCode = l.TestCode,
            TestName = l.TestName,
            Category = l.Category,
            Qty = l.Qty,
            Fee = l.Fee
        };

        public RadiologyRequestLine ToEntity() => new()
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
