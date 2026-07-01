using System.ComponentModel.DataAnnotations;
using System.Globalization;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Surgery;

public class IndexModel : ClinicFormPageModel
{
    private readonly DoctorSurgeryService _service;

    public IndexModel(ClinicContextService clinicContext, DoctorSurgeryService service) : base(clinicContext)
    {
        _service = service;
    }

    [BindProperty]
    public SurgeryInput Input { get; set; } = new();

    public List<DoctorSurgery> Records { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        if (ShouldOpenExistingRecordOnGet())
            await LoadRecord(clinicId.Value, RecordId!.Value);
        else
            await PrepareNew(clinicId.Value);
        ViewData["OpenPatientSelect"] = true;
        SetFormViewData("Doctor Surgery", null, null, Input.UpdatedAt, Input.SurgeryNo.ToString());
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
        {
            Records = Records.Where(r =>
                (r.PatientName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                (r.DoctorName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                r.SurgeryNo.ToString().Contains(Search)).ToList();
        }
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = SurgeryInput.FromEntity(item);
        Input.Specialty = await ResolveDoctorSpecialtyAsync(clinicId, Input.DoctorName, Input.Specialty);
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _service.ListAsync(clinicId);
        var next = (all.Count > 0 ? all.Max(r => r.SurgeryNo) : 0) + 1;
        Input = new SurgeryInput { SurgeryNo = next, RecordDate = DateTime.Today };
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();

        await BackfillFromPatientAsync(clinicId.Value);

        if (!ModelState.IsValid)
        {
            await LoadAsync(clinicId.Value);
            ViewData["OpenPatientSelect"] = true;
            SetFormViewData("Doctor Surgery", null, null, Input.UpdatedAt, Input.SurgeryNo.ToString());
            return Page();
        }

        ResolveRecordIdForSave();
        var saved = await _service.SaveAsync(clinicId.Value, Input.ToEntity(RecordIdForSave), UserName);
        return RedirectAfterSave(saved.Id);
    }

    protected override async Task ReloadAfterSaveFailureAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return;
        await LoadAsync(clinicId.Value);
        ViewData["OpenPatientSelect"] = true;
        SetFormViewData("Doctor Surgery", null, null, Input.UpdatedAt, Input.SurgeryNo.ToString());
    }

    private async Task BackfillFromPatientAsync(Guid clinicId)
    {
        var patient = await FindPatientAsync(clinicId, Input.PatientName, Input.PatientBarcode);
        if (patient is null) return;

        Input.PatientRecordId = patient.Id;
        Input.PatientName ??= patient.FullName;
        Input.PatientBarcode ??= patient.PatientNo;
        Input.Age = patient.AgeYears;
        Input.City ??= patient.City;
        Input.NationalId ??= patient.NationalId;
        Input.Phone ??= patient.Phone;
        Input.MotherName ??= patient.MotherName;
        Input.DoctorRecordId = patient.DoctorRecordId;
        Input.DoctorName ??= patient.DoctorName;
        Input.Specialty = await ResolveDoctorSpecialtyAsync(clinicId, patient.DoctorName, patient.Specialty);
    }

    private async Task<IActionResult> NewCoreAsync()
    {
        RecordId = null;
        return RedirectToNewForm();
    }

    private async Task<IActionResult> DeleteCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null || !RecordId.HasValue) return RedirectToPage();
        return await SafeDeleteAsync(
            () => _service.DeleteAsync(clinicId.Value, RecordId.Value, UserName),
            async () => await LoadAsync(clinicId.Value));
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

    public sealed class SurgeryInput
    {
        public int SurgeryNo { get; set; }
        public DateTime RecordDate { get; set; } = DateTime.Today;
        public DateTime? SurgeryDate { get; set; }
        public string? SurgeryTime { get; set; }
        public Guid? PatientRecordId { get; set; }

        [Required(ErrorMessage = "Patient name is required.")]
        public string? PatientName { get; set; }
        public string? PatientBarcode { get; set; }
        public int? Age { get; set; }
        public string? City { get; set; }
        public string? NationalId { get; set; }
        public string? Phone { get; set; }
        public string? MotherName { get; set; }
        public Guid? DoctorRecordId { get; set; }
        public string? DoctorName { get; set; }
        public string? Specialty { get; set; }
        public string? TypeOfSurgery { get; set; }
        public string? Classify { get; set; }
        public string? SurgeryName { get; set; }
        public decimal InitialAmount { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public static SurgeryInput FromEntity(DoctorSurgery e) => new()
        {
            SurgeryNo = e.SurgeryNo,
            RecordDate = e.RecordDate,
            SurgeryDate = e.SurgeryDate,
            SurgeryTime = e.SurgeryTime?.ToString(@"hh\:mm"),
            PatientRecordId = e.PatientRecordId,
            PatientName = e.PatientName,
            PatientBarcode = e.PatientBarcode,
            Age = e.Age,
            City = e.City,
            NationalId = e.NationalId,
            Phone = e.Phone,
            MotherName = e.MotherName,
            DoctorRecordId = e.DoctorRecordId,
            DoctorName = e.DoctorName,
            Specialty = e.Specialty,
            TypeOfSurgery = e.TypeOfSurgery,
            Classify = e.Classify,
            SurgeryName = e.SurgeryName,
            InitialAmount = e.InitialAmount,
            UpdatedAt = e.UpdatedAt
        };

        public DoctorSurgery ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            SurgeryNo = SurgeryNo,
            RecordDate = RecordDate,
            SurgeryDate = SurgeryDate,
            SurgeryTime = ParseTime(SurgeryTime),
            PatientRecordId = PatientRecordId,
            PatientName = PatientName?.Trim(),
            PatientBarcode = PatientBarcode?.Trim(),
            Age = Age,
            City = City?.Trim(),
            NationalId = NationalId?.Trim(),
            Phone = Phone?.Trim(),
            MotherName = MotherName?.Trim(),
            DoctorRecordId = DoctorRecordId,
            DoctorName = DoctorName?.Trim(),
            Specialty = Specialty?.Trim(),
            TypeOfSurgery = TypeOfSurgery?.Trim(),
            Classify = Classify?.Trim(),
            SurgeryName = SurgeryName?.Trim(),
            InitialAmount = InitialAmount,
            UpdatedAt = UpdatedAt
        };

        private static TimeSpan? ParseTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var t) ? t : null;
        }
    }
}
