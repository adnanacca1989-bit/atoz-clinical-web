using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Prescriptions;

public class IndexModel : ClinicFormPageModel
{
    private readonly PrescriptionService _service;
    private readonly PharmacyItemRegistrationService _items;
    private const int DefaultMedicationLineCount = 6;

    public IndexModel(
        ClinicContextService clinicContext,
        PrescriptionService service,
        PharmacyItemRegistrationService items) : base(clinicContext)
    {
        _service = service;
        _items = items;
    }

    [BindProperty]
    public PrescriptionInput Input { get; set; } = new();

    [BindProperty]
    public List<ChronicDiseaseInput> ChronicDiseases { get; set; } = [];

    [BindProperty]
    public List<PrescriptionMedicationInput> Medications { get; set; } = [];

    public List<Prescription> Records { get; private set; } = [];
    public List<PharmacyItem> RegisteredItems { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        RegisteredItems = await _items.ListActiveAsync(clinicId.Value);
        if (ShouldOpenExistingRecordOnGet())
            await LoadRecord(clinicId.Value, RecordId!.Value);
        else
            await PrepareNew(clinicId.Value);
        ViewData["OpenPatientSelect"] = true;
        ViewData["ShowAddLines"] = true;
        SetFormViewData("Doctor's Prescription", null, null, Input.UpdatedAt);
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
                r.PrescriptionNo.ToString().Contains(Search)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = PrescriptionInput.FromEntity(item);
        Input.Specialty = await ResolveDoctorSpecialtyAsync(clinicId, Input.DoctorName, Input.Specialty);
        ChronicDiseases = DeserializeChronic(item.ChronicDiseasesJson);
        Medications = item.Lines.OrderBy(l => l.LineNo).Select(PrescriptionMedicationInput.FromEntity).ToList();
        EnsureMedicationRows();
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _service.ListAsync(clinicId);
        var next = (all.Count > 0 ? all.Max(p => p.PrescriptionNo) : 0) + 1;
        Input = new PrescriptionInput { PrescriptionNo = next, DatePrescription = DateTime.Today, Gender = "Male" };
        ChronicDiseases = ClinicLookup.ChronicDiseaseTypes
            .Select(t => new ChronicDiseaseInput { DiseaseType = t })
            .ToList();
        Medications = Enumerable.Range(1, DefaultMedicationLineCount)
            .Select(i => new PrescriptionMedicationInput { LineNo = i })
            .ToList();
    }

    private void EnsureMedicationRows()
    {
        while (Medications.Count < DefaultMedicationLineCount)
            Medications.Add(new PrescriptionMedicationInput { LineNo = Medications.Count + 1 });
        for (var i = 0; i < Medications.Count; i++)
            Medications[i].LineNo = i + 1;
    }

    private async Task LoadFormContextAsync(Guid clinicId)
    {
        await LoadAsync(clinicId);
        RegisteredItems = await _items.ListActiveAsync(clinicId);
        ViewData["OpenPatientSelect"] = true;
        ViewData["ShowAddLines"] = true;
        SetFormViewData("Doctor's Prescription", null, null, Input.UpdatedAt);
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();

        await BackfillFromPatientAsync(clinicId.Value);

        if (!ModelState.IsValid)
        {
            await LoadFormContextAsync(clinicId.Value);
            return Page();
        }

        ResolveRecordIdForSave();
        var entity = Input.ToEntity(RecordIdForSave);
        entity.ChronicDiseasesJson = JsonSerializer.Serialize(ChronicDiseases.Where(c => !string.IsNullOrWhiteSpace(c.Details)));
        var lines = Medications
            .Where(m => !string.IsNullOrWhiteSpace(m.MedicineName))
            .Select(m => m.ToEntity())
            .ToList();
        var saved = await _service.SaveAsync(clinicId.Value, entity, lines, UserName);
        return RedirectAfterSave(saved.Id);
    }

    protected override async Task ReloadAfterSaveFailureAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return;
        await LoadFormContextAsync(clinicId.Value);
    }

    private async Task BackfillFromPatientAsync(Guid clinicId)
    {
        var patient = await FindPatientAsync(clinicId, Input.PatientName, Input.PatientBarcode);
        if (patient is null) return;

        Input.PatientName ??= patient.FullName;
        Input.PatientBarcode ??= patient.PatientNo;
        Input.Age ??= patient.AgeYears;
        Input.Gender ??= patient.Gender;
        Input.DoctorName ??= patient.DoctorName;
        Input.Specialty = await ResolveDoctorSpecialtyAsync(clinicId, patient.DoctorName, patient.Specialty ?? Input.Specialty);
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

    private static List<ChronicDiseaseInput> DeserializeChronic(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ClinicLookup.ChronicDiseaseTypes.Select(t => new ChronicDiseaseInput { DiseaseType = t }).ToList();
        try
        {
            var saved = JsonSerializer.Deserialize<List<ChronicDiseaseInput>>(json) ?? [];
            return ClinicLookup.ChronicDiseaseTypes.Select(t =>
            {
                var match = saved.FirstOrDefault(s => s.DiseaseType == t);
                return match ?? new ChronicDiseaseInput { DiseaseType = t };
            }).ToList();
        }
        catch
        {
            return ClinicLookup.ChronicDiseaseTypes.Select(t => new ChronicDiseaseInput { DiseaseType = t }).ToList();
        }
    }

    public sealed class PrescriptionInput
    {
        public int PrescriptionNo { get; set; }

        [Required(ErrorMessage = "Patient Name is required.")]
        [Display(Name = "Patient Name")]
        public string? PatientName { get; set; }

        [Required(ErrorMessage = "Age is required.")]
        [Range(0, 150, ErrorMessage = "Age is required.")]
        public int? Age { get; set; }

        [Required(ErrorMessage = "Gender is required.")]
        public string? Gender { get; set; }

        [Required(ErrorMessage = "Doctor Name is required.")]
        [Display(Name = "Doctor Name")]
        public string? DoctorName { get; set; }

        [Required(ErrorMessage = "Specialty is required.")]
        public string? Specialty { get; set; }

        public string? PatientBarcode { get; set; }

        [Required(ErrorMessage = "Date Prescription is required.")]
        [Display(Name = "Date Prescription")]
        public DateTime DatePrescription { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "Name of Disease is required.")]
        [Display(Name = "Name of Disease")]
        public string? DiseaseName { get; set; }

        [Required(ErrorMessage = "Diagnosis Text is required.")]
        [Display(Name = "Diagnosis Text")]
        public string? DiagnosisText { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static PrescriptionInput FromEntity(Prescription p) => new()
        {
            PrescriptionNo = p.PrescriptionNo,
            PatientName = p.PatientName,
            Age = p.Age,
            Gender = p.Gender,
            DoctorName = p.DoctorName,
            Specialty = p.Specialty,
            DatePrescription = p.DatePrescription,
            DiseaseName = p.DiseaseName,
            DiagnosisText = p.DiagnosisText,
            UpdatedAt = p.UpdatedAt
        };

        public Prescription ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            PrescriptionNo = PrescriptionNo,
            PatientName = PatientName,
            Age = Age,
            Gender = Gender,
            DoctorName = DoctorName,
            Specialty = Specialty,
            DatePrescription = DatePrescription,
            DiseaseName = DiseaseName,
            DiagnosisText = DiagnosisText
        };
    }

    public sealed class ChronicDiseaseInput
    {
        public string DiseaseType { get; set; } = string.Empty;
        public string? Details { get; set; }
    }

    public sealed class PrescriptionMedicationInput
    {
        public int LineNo { get; set; }
        public Guid? PharmacyItemId { get; set; }
        public string? MedicineName { get; set; }
        public string? MedicationForm { get; set; }
        public string? Dose { get; set; }
        public string? Unit { get; set; }
        public string? Frequency { get; set; }
        public string? Duration { get; set; }
        public string? Instruction { get; set; }

        public static PrescriptionMedicationInput FromEntity(PrescriptionLine line) => new()
        {
            LineNo = line.LineNo,
            PharmacyItemId = line.PharmacyItemId,
            MedicineName = line.MedicineName,
            MedicationForm = line.MedicationForm,
            Dose = line.Dose,
            Unit = line.Unit,
            Frequency = line.Frequency,
            Duration = line.Duration,
            Instruction = line.Instruction
        };

        public PrescriptionLine ToEntity() => new()
        {
            LineNo = LineNo,
            PharmacyItemId = PharmacyItemId,
            MedicineName = MedicineName,
            MedicationForm = MedicationForm,
            Dose = Dose,
            Unit = Unit,
            Frequency = Frequency,
            Duration = Duration,
            Instruction = Instruction
        };
    }
}
