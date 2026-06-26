using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.ServiceIncomes;

public class RequestModel : ClinicFormPageModel
{
    private readonly ServiceIncomeRequestService _service;
    private readonly ServiceIncomeService _catalog;
    private const int DefaultLineCount = 6;

    public RequestModel(
        ClinicContextService clinicContext,
        ServiceIncomeRequestService service,
        ServiceIncomeService catalog) : base(clinicContext)
    {
        _service = service;
        _catalog = catalog;
    }

    [BindProperty]
    public ServiceIncomeRequestInput Input { get; set; } = new();

    [BindProperty]
    public List<ServiceIncomeRequestLineInput> Lines { get; set; } = [];

    public List<ServiceIncomeRequest> Records { get; private set; } = [];
    public List<ServiceIncome> RegisteredServices { get; private set; } = [];

    public decimal LineTotal => Lines.Sum(l => l.Qty * l.Fee);

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        RegisteredServices = await _catalog.ListAsync(clinicId.Value);
        if (ShouldLoadExistingRecord())
            await LoadRecord(clinicId.Value, RecordId!.Value);
        else
            await PrepareNew(clinicId.Value);
        ViewData["OpenPatientSelect"] = true;
        ViewData["ShowAddLines"] = true;
        SetFormViewData("Service Income Request", null, null, Input.UpdatedAt);
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
        Input = ServiceIncomeRequestInput.FromEntity(item);
        Input.Specialty = await ResolveDoctorSpecialtyAsync(clinicId, Input.DoctorName, Input.Specialty);
        Lines = item.Lines.OrderBy(l => l.LineNo).Select(ServiceIncomeRequestLineInput.FromEntity).ToList();
        EnsureLineRows();
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _service.ListAsync(clinicId);
        var next = (all.Count > 0 ? all.Max(r => r.RequestNo) : 0) + 1;
        Input = new ServiceIncomeRequestInput { RequestNo = next, RequestDate = DateTime.Today, Gender = "Male" };
        Lines = CreateEmptyLines();
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        ResolveRecordIdForSave();

        if (!ModelState.IsValid)
        {
            await LoadAsync(clinicId.Value);
            RegisteredServices = await _catalog.ListAsync(clinicId.Value);
            ViewData["OpenPatientSelect"] = true;
            ViewData["ShowAddLines"] = true;
            SetFormViewData("Service Income Request", null, null, Input.UpdatedAt);
            EnsureLineRows();
            return Page();
        }

        var entity = Input.ToEntity(RecordIdForSave);
        var lines = Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.ServiceName))
            .Select(l => l.ToEntity())
            .ToList();
        var saved = await _service.SaveAsync(clinicId.Value, entity, lines, UserName);
        return RedirectAfterSave(saved.Id);
    }

    private Task<IActionResult> NewCoreAsync() => Task.FromResult<IActionResult>(RedirectToNewForm());

    private async Task<IActionResult> DeleteCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null || !RecordId.HasValue) return RedirectToPage();
        return await SafeDeleteAsync(
            () => _service.DeleteAsync(clinicId.Value, RecordId.Value, UserName),
            async () =>
            {
                await LoadAsync(clinicId.Value);
                RegisteredServices = await _catalog.ListAsync(clinicId.Value);
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
            Lines.Add(new ServiceIncomeRequestLineInput { LineNo = Lines.Count + 1 });
    }

    private static List<ServiceIncomeRequestLineInput> CreateEmptyLines() =>
        Enumerable.Range(1, DefaultLineCount).Select(i => new ServiceIncomeRequestLineInput { LineNo = i }).ToList();

    public sealed class ServiceIncomeRequestInput
    {
        public int RequestNo { get; set; }
        public DateTime RequestDate { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "Patient Name is required.")]
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
        public string? DoctorName { get; set; }

        [Required(ErrorMessage = "Specialty is required.")]
        public string? Specialty { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public static ServiceIncomeRequestInput FromEntity(ServiceIncomeRequest r) => new()
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

        public ServiceIncomeRequest ToEntity(Guid? id) => new()
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

    public sealed class ServiceIncomeRequestLineInput
    {
        public int LineNo { get; set; }
        public int? ServiceNo { get; set; }
        public string? ServiceName { get; set; }
        public string? AccountName { get; set; }
        public int Qty { get; set; } = 1;
        public decimal Fee { get; set; }

        public decimal Total => Qty * Fee;

        public static ServiceIncomeRequestLineInput FromEntity(ServiceIncomeRequestLine l) => new()
        {
            LineNo = l.LineNo,
            ServiceNo = l.ServiceNo,
            ServiceName = l.ServiceName,
            AccountName = l.AccountName,
            Qty = l.Qty,
            Fee = l.Fee
        };

        public ServiceIncomeRequestLine ToEntity() => new()
        {
            LineNo = LineNo,
            ServiceNo = ServiceNo,
            ServiceName = ServiceName,
            AccountName = AccountName,
            Qty = Qty,
            Fee = Fee,
            Total = Qty * Fee
        };
    }
}
