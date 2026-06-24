using System.ComponentModel.DataAnnotations;

using AtoZClinical.Core.Entities;

using AtoZClinical.Infrastructure.Services;

using AtoZClinical.Web.Services;

using Microsoft.AspNetCore.Mvc;



namespace AtoZClinical.Web.Pages.PatientRegistration;



public class IndexModel : ClinicFormPageModel

{

    private readonly PatientService _service;
    private readonly PatientVisitHistoryService _history;

    public IndexModel(ClinicContextService clinicContext, PatientService service, PatientVisitHistoryService history) : base(clinicContext)
    {
        _service = service;
        _history = history;
    }



    [BindProperty]

    public PatientInput Input { get; set; } = new();



    public List<Patient> Records { get; private set; } = [];

    public int TotalPatients { get; private set; }

    public int TodayPatients { get; private set; }

    public int ActivePatients { get; private set; }

    public int InactivePatients { get; private set; }



    public async Task<IActionResult> OnGetAsync()

    {

        var clinicId = await RequireClinicIdAsync();

        if (clinicId is null) return Forbid();

        await LoadAsync(clinicId.Value);

        if (RecordId.HasValue)

            await LoadRecord(clinicId.Value, RecordId.Value);

        else if (NewRecord)

            await PrepareNew(clinicId.Value);

        else if (Records.Count > 0 && string.IsNullOrEmpty(Input.PatientNo))

            await LoadRecord(clinicId.Value, Records[0].Id);

        else

            await PrepareNew(clinicId.Value);

        ViewData["ShowHistory"] = true;

        ViewData["ShowPatientCard"] = true;

        ViewData["OpenPatientSelect"] = true;

        SetFormViewData("Patient Registration", Input.CreatedBy, Input.UpdatedBy, Input.UpdatedAt);

        return Page();

    }



    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync();

    public Task<IActionResult> OnPostNewAsync() => NewCoreAsync();

    public Task<IActionResult> OnPostClearAsync() => NewCoreAsync();

    public Task<IActionResult> OnPostDeleteAsync() => DeleteCoreAsync();

    public Task<IActionResult> OnPostBackAsync() => NavigateCoreAsync(-1);

    public Task<IActionResult> OnPostNextAsync() => NavigateCoreAsync(1);

    public async Task<IActionResult> OnGetHistoryAsync(string? patientNo, string? patientName, string? nationalId, string? phone)
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();

        var summary = await _history.GetHistoryAsync(clinicId.Value, patientNo, patientName, nationalId, phone);
        return new JsonResult(new
        {
            rows = summary.Rows.Select(r => new
            {
                visitDate = r.VisitDate.ToString("yyyy-MM-dd"),
                doctorName = r.DoctorName,
                totalRevenue = r.TotalRevenue,
                amountReceived = r.AmountReceived
            }),
            grandTotalRevenue = summary.GrandTotalRevenue,
            grandAmountReceived = summary.GrandAmountReceived
        });
    }



    private async Task LoadAsync(Guid clinicId)

    {

        Records = await _service.ListAsync(clinicId, Search);

        TotalPatients = await _service.CountTotalAsync(clinicId);

        TodayPatients = await _service.CountTodayAsync(clinicId);

        ActivePatients = await _service.CountActiveAsync(clinicId);

        InactivePatients = await _service.CountInactiveAsync(clinicId);

    }



    private async Task LoadRecord(Guid clinicId, Guid id)

    {

        var p = await _service.GetAsync(clinicId, id);

        if (p is null) return;

        RecordId = p.Id;

        Input = PatientInput.FromEntity(p);

    }



    private async Task PrepareNew(Guid clinicId)

    {

        RecordId = null;

        var nextNo = await _service.NextPatientNoAsync(clinicId);

        var nextAppt = await _service.NextAppointmentIdAsync(clinicId);

        Input = new PatientInput

        {

            PatientNo = nextNo,

            AppointmentId = nextAppt,

            Status = "Pending",

            Gender = "Male",

            VisitNumber = "1"

        };

    }



    private async Task<IActionResult> SaveCoreAsync()

    {

        var clinicId = await RequireClinicIdAsync();

        if (clinicId is null) return Forbid();



        if (!ModelState.IsValid)

        {

            await LoadAsync(clinicId.Value);

            ViewData["ShowHistory"] = true;

            ViewData["ShowPatientCard"] = true;

            SetFormViewData("Patient Registration", Input.CreatedBy, Input.UpdatedBy, Input.UpdatedAt);

            return Page();

        }



        var isNew = !RecordId.HasValue;

        if (isNew)
        {
            var nextVisit = await _service.GetNextVisitNumberAsync(
                clinicId.Value, Input.NationalId, Input.Phone, null, Input.PatientName);
            Input.VisitNumber = nextVisit.ToString();
        }
        else if (string.IsNullOrWhiteSpace(Input.VisitNumber))
        {
            var nextVisit = await _service.GetNextVisitNumberAsync(
                clinicId.Value, Input.NationalId, Input.Phone, RecordId, Input.PatientName);
            Input.VisitNumber = nextVisit.ToString();
        }



        if (string.IsNullOrWhiteSpace(Input.AppointmentId))

            Input.AppointmentId = await _service.NextAppointmentIdAsync(clinicId.Value);



        var entity = Input.ToEntity(RecordId);

        try
        {
            var saved = await _service.SaveAsync(clinicId.Value, entity, UserName);
            return RedirectAfterSave(saved.Id);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadAsync(clinicId.Value);
            ViewData["ShowHistory"] = true;
            ViewData["ShowPatientCard"] = true;
            SetFormViewData("Patient Registration", Input.CreatedBy, Input.UpdatedBy, Input.UpdatedAt);
            return Page();
        }

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
            () => _service.DeleteAsync(clinicId.Value, RecordId.Value),
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



    public sealed class PatientInput

    {

        public string PatientNo { get; set; } = string.Empty;



        [Required(ErrorMessage = "Patient Name is required.")]

        [Display(Name = "Patient Name")]

        public string PatientName { get; set; } = string.Empty;



        [Required(ErrorMessage = "Gender is required.")]

        public string? Gender { get; set; } = "Male";



        [Required(ErrorMessage = "Date of Birth is required.")]

        [Display(Name = "Date of Birth")]

        public DateTime? DateOfBirth { get; set; }



        [Required(ErrorMessage = "Phone is required.")]

        public string? Phone { get; set; }



        [Required(ErrorMessage = "City is required.")]

        public string? City { get; set; }



        [Required(ErrorMessage = "Doctor Name is required.")]

        [Display(Name = "Doctor Name")]

        public string? DoctorName { get; set; }



        [Required(ErrorMessage = "Specialty is required.")]

        public string? Specialty { get; set; }



        public string? NationalId { get; set; }

        public string? Address { get; set; }

        public string? BloodGroup { get; set; }

        public string? MarriedStatus { get; set; }

        public string? MotherName { get; set; }

        public string? EmergencyContact { get; set; }

        public string? HealthInsuranceName { get; set; }

        public string? HealthInsuranceNumber { get; set; }

        public string? AppointmentId { get; set; }

        public string? VisitNumber { get; set; }



        [Required(ErrorMessage = "Appointment Date is required.")]

        [Display(Name = "Appointment Date")]

        public DateTime? AppointmentDate { get; set; }



        [Required(ErrorMessage = "Appointment Time is required.")]

        [Display(Name = "Appointment Time")]

        public TimeSpan? AppointmentTime { get; set; }



        public string Status { get; set; } = "Active";

        public string? CreatedBy { get; set; }

        public string? UpdatedBy { get; set; }

        public DateTime? UpdatedAt { get; set; }



        public int? DisplayAge => DateOfBirth.HasValue

            ? Math.Max(0, (int)((DateTime.Today - DateOfBirth.Value.Date).TotalDays / 365.25))

            : null;



        public static PatientInput FromEntity(Patient p) => new()

        {

            PatientNo = p.PatientNo,

            PatientName = p.FirstName,

            Gender = p.Gender,

            DateOfBirth = p.DateOfBirth,

            Phone = p.Phone,

            City = p.City,

            DoctorName = p.DoctorName,

            Specialty = p.Specialty,

            NationalId = p.NationalId,

            Address = p.Address,

            BloodGroup = p.BloodGroup,

            MarriedStatus = p.MarriedStatus,

            MotherName = p.MotherName,

            EmergencyContact = p.EmergencyContact,

            HealthInsuranceName = p.HealthInsuranceName,

            HealthInsuranceNumber = p.HealthInsuranceNumber,

            AppointmentId = p.AppointmentId,

            VisitNumber = p.VisitNumber,

            AppointmentDate = p.AppointmentDate,

            AppointmentTime = p.AppointmentTime,

            Status = p.Status,

            CreatedBy = p.CreatedBy,

            UpdatedBy = p.UpdatedBy,

            UpdatedAt = p.UpdatedAt

        };



        public Patient ToEntity(Guid? id) => new()

        {

            Id = id ?? Guid.Empty,

            PatientNo = PatientNo.Trim(),

            FirstName = PatientName.Trim(),

            LastName = string.Empty,

            Gender = Gender,

            DateOfBirth = DateOfBirth,

            Phone = Phone,

            City = City,

            DoctorName = DoctorName,

            Specialty = Specialty,

            NationalId = NationalId,

            Address = Address,

            BloodGroup = BloodGroup,

            MarriedStatus = MarriedStatus,

            MotherName = MotherName,

            EmergencyContact = EmergencyContact,

            HealthInsuranceName = HealthInsuranceName,

            HealthInsuranceNumber = HealthInsuranceNumber,

            AppointmentId = AppointmentId,

            VisitNumber = VisitNumber,

            AppointmentDate = AppointmentDate,

            AppointmentTime = AppointmentTime,

            Status = Status

        };

    }

}


