using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Pharmacy;

public class RequestModel : ClinicFormPageModel
{
    private readonly PharmacyRequestService _service;
    private readonly PharmacyItemRegistrationService _items;
    private const int DefaultLineCount = 6;

    public RequestModel(ClinicContextService clinicContext, PharmacyRequestService service, PharmacyItemRegistrationService items) : base(clinicContext)
    {
        _service = service;
        _items = items;
    }

    [BindProperty]
    public PharmacyRequestInput Input { get; set; } = new();

    [BindProperty]
    public List<PharmacyRequestLineInput> Lines { get; set; } = [];

    public List<PharmacyRequest> Records { get; private set; } = [];
    public List<PharmacyItem> RegisteredItems { get; private set; } = [];

    public decimal LineTotal => Lines.Sum(l => l.Total);

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        RegisteredItems = await _items.ListActiveAsync(clinicId.Value);
        if (ShouldLoadExistingRecord())
            await LoadRecord(clinicId.Value, RecordId!.Value);
        else
            await PrepareNew(clinicId.Value);
        SetFormViewData("Pharmacy Request", null, null, Input.UpdatedAt);
        ViewData["OpenPatientSelect"] = true;
        ViewData["ShowAddLines"] = true;
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
        Input = PharmacyRequestInput.FromEntity(item);
        Input.Specialty = await ResolveDoctorSpecialtyAsync(clinicId, Input.DoctorName, Input.Specialty);
        Lines = item.Lines.OrderBy(l => l.LineNo).Select(PharmacyRequestLineInput.FromEntity).ToList();
        EnsureLineRows();
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _service.ListAsync(clinicId);
        var next = (all.Count > 0 ? all.Max(r => r.RequestNo) : 0) + 1;
        Input = new PharmacyRequestInput { RequestNo = next, RequestDate = DateTime.Today, Gender = "Male" };
        Lines = CreateEmptyLines();
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        if (!ModelState.IsValid)
        {
            await ReloadFormAsync(clinicId.Value);
            return Page();
        }
        var entity = Input.ToEntity(RecordId);
        var lines = Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.MedicineCode) || !string.IsNullOrWhiteSpace(l.MedicineName))
            .Select(l => l.ToEntity())
            .ToList();
        var saved = await _service.SaveAsync(clinicId.Value, entity, lines, UserName);
        return RedirectAfterSave(saved.Id);
    }

    private Task<IActionResult> NewCoreAsync() => Task.FromResult(RedirectToNewForm());

    private async Task ReloadFormAsync(Guid clinicId)
    {
        await LoadAsync(clinicId);
        RegisteredItems = await _items.ListActiveAsync(clinicId);
        EnsureLineRows();
        SetFormViewData("Pharmacy Request", null, null, Input.UpdatedAt);
        ViewData["OpenPatientSelect"] = true;
        ViewData["ShowAddLines"] = true;
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
            Lines.Add(new PharmacyRequestLineInput { LineNo = Lines.Count + 1 });
    }

    private static List<PharmacyRequestLineInput> CreateEmptyLines() =>
        Enumerable.Range(1, DefaultLineCount).Select(i => new PharmacyRequestLineInput { LineNo = i }).ToList();

    public sealed class PharmacyRequestInput
    {
        public int RequestNo { get; set; }

        [Required(ErrorMessage = "Request Date is required.")]
        public DateTime RequestDate { get; set; } = DateTime.Today;

        public int? PrescriptionNo { get; set; }

        [Required(ErrorMessage = "Patient Name is required.")]
        public string? PatientName { get; set; }

        public string? PatientId { get; set; }

        [Required(ErrorMessage = "Age is required.")]
        public int? Age { get; set; }

        [Required(ErrorMessage = "Sex is required.")]
        public string? Gender { get; set; }

        public string? Phone { get; set; }
        public string? City { get; set; }

        [Required(ErrorMessage = "Doctor is required.")]
        public string? DoctorName { get; set; }

        [Required(ErrorMessage = "Specialty is required.")]
        public string? Specialty { get; set; }
        public string? Notes { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static PharmacyRequestInput FromEntity(PharmacyRequest r) => new()
        {
            RequestNo = r.RequestNo,
            RequestDate = r.RequestDate,
            PrescriptionNo = r.PrescriptionNo,
            PatientName = r.PatientName,
            PatientId = r.PatientId,
            Age = r.Age,
            Gender = r.Gender,
            Phone = r.Phone,
            City = r.City,
            DoctorName = r.DoctorName,
            Specialty = r.Specialty,
            Notes = r.Notes,
            UpdatedAt = r.UpdatedAt
        };

        public PharmacyRequest ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            RequestNo = RequestNo,
            RequestDate = RequestDate,
            PrescriptionNo = PrescriptionNo,
            PatientName = PatientName,
            PatientId = PatientId,
            Age = Age,
            Gender = Gender,
            Phone = Phone,
            City = City,
            DoctorName = DoctorName,
            Specialty = Specialty,
            Notes = Notes
        };
    }

    public sealed class PharmacyRequestLineInput
    {
        public int LineNo { get; set; }
        public string? Barcode { get; set; }
        public string? MedicineCode { get; set; }
        public string? MedicineName { get; set; }
        public string? Dosage { get; set; }
        public string? Uom { get; set; }
        public int Qty { get; set; } = 1;
        public decimal UnitPrice { get; set; }

        public decimal Total => Qty * UnitPrice;

        public static PharmacyRequestLineInput FromEntity(PharmacyRequestLine l) => new()
        {
            LineNo = l.LineNo,
            Barcode = l.Barcode,
            MedicineCode = l.MedicineCode,
            MedicineName = l.MedicineName,
            Dosage = l.Dosage,
            Uom = l.Uom,
            Qty = l.Qty,
            UnitPrice = l.UnitPrice
        };

        public PharmacyRequestLine ToEntity() => new()
        {
            LineNo = LineNo,
            Barcode = Barcode,
            MedicineCode = MedicineCode,
            MedicineName = MedicineName,
            Dosage = Dosage,
            Uom = Uom,
            Qty = Qty,
            UnitPrice = UnitPrice,
            Total = Qty * UnitPrice
        };
    }
}
