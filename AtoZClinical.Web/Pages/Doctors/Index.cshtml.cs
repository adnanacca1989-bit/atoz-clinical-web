using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace AtoZClinical.Web.Pages.Doctors;

public class IndexModel : ClinicFormPageModel
{
    private readonly DoctorService _service;

    public IndexModel(ClinicContextService clinicContext, DoctorService service) : base(clinicContext)
    {
        _service = service;
    }

    protected override async Task ReloadAfterSaveFailureAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return;
        await LoadAsync(clinicId.Value);
        SetFormViewData("Doctor Registration", Input.CreatedBy, Input.UpdatedBy, Input.UpdatedAt);
    }

    [BindProperty]
    public DoctorInput Input { get; set; } = new();

    public List<Doctor> Records { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        if (RecordId.HasValue)
            await LoadRecord(clinicId.Value, RecordId.Value);
        else if (NewRecord)
            await PrepareNew(clinicId.Value);
        else if (Records.Count > 0 && Input.DoctorNo == 0)
            await LoadRecord(clinicId.Value, Records[0].Id);
        else
            await PrepareNew(clinicId.Value);
        SetFormViewData("Doctor Registration", Input.CreatedBy, Input.UpdatedBy, Input.UpdatedAt);
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
            Records = Records.Where(r => r.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                                           r.Specialty?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var d = await _service.GetAsync(clinicId, id);
        if (d is null) return;
        RecordId = d.Id;
        Input = DoctorInput.FromEntity(d);
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var next = await _service.NextNoAsync(clinicId);
        Input = new DoctorInput { DoctorNo = next, Status = "Active", Specialty = "ENT" };
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();

        if (!ModelState.IsValid)
        {
            if (!ModelState.Values.SelectMany(v => v.Errors).Any())
                ModelState.AddModelError(string.Empty, "Please fill in all required fields (Doctor Name and Consultation Fee).");
            await LoadAsync(clinicId.Value);
            SetFormViewData("Doctor Registration", Input.CreatedBy, Input.UpdatedBy, Input.UpdatedAt);
            return Page();
        }

        ResolveRecordIdForSave();

        var entity = Input.ToEntity(RecordIdForSave);

        try
        {
            var saved = await _service.SaveAsync(clinicId.Value, entity, UserName);
            return RedirectAfterSave(saved.Id);
        }
        catch (DbUpdateException ex) when (DoctorService.IsDuplicateDoctorNo(ex))
        {
            var preserved = Input;
            await PrepareNew(clinicId.Value);
            Input.Name = preserved.Name;
            Input.Specialty = preserved.Specialty;
            Input.Phone = preserved.Phone;
            Input.Email = preserved.Email;
            Input.ConsultationFee = preserved.ConsultationFee;
            Input.Status = preserved.Status;
            ModelState.AddModelError(string.Empty, "That doctor ID was just taken. The next available ID is shown — click Add again.");
            await LoadAsync(clinicId.Value);
            SetFormViewData("Doctor Registration", Input.CreatedBy, Input.UpdatedBy, Input.UpdatedAt);
            return Page();
        }
        catch (DbUpdateException ex)
        {
            ModelState.AddModelError(string.Empty, "Could not save this doctor. Please refresh the page and try again.");
            await LoadAsync(clinicId.Value);
            SetFormViewData("Doctor Registration", Input.CreatedBy, Input.UpdatedBy, Input.UpdatedAt);
            HttpContext.RequestServices.GetService<ILogger<IndexModel>>()?
                .LogError(ex, "Doctor DbUpdate failed for clinic {ClinicId}: {Message}", clinicId, ex.InnerException?.Message ?? ex.Message);
            return Page();
        }
        catch (InvalidOperationException ex)
        {
            var preserved = Input;
            await PrepareNew(clinicId.Value);
            Input.Name = preserved.Name;
            Input.Specialty = preserved.Specialty;
            Input.Phone = preserved.Phone;
            Input.Email = preserved.Email;
            Input.ConsultationFee = preserved.ConsultationFee;
            Input.Status = preserved.Status;
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadAsync(clinicId.Value);
            SetFormViewData("Doctor Registration", Input.CreatedBy, Input.UpdatedBy, Input.UpdatedAt);
            return Page();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, "Could not save this doctor. Please check all required fields and try again.");
            await LoadAsync(clinicId.Value);
            SetFormViewData("Doctor Registration", Input.CreatedBy, Input.UpdatedBy, Input.UpdatedAt);
            HttpContext.RequestServices.GetService<ILogger<IndexModel>>()?
                .LogError(ex, "Doctor save failed for clinic {ClinicId}", clinicId);
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

    public sealed class DoctorInput
    {
        public int DoctorNo { get; set; }

        [Required(ErrorMessage = "Doctor Name is required.")]
        [Display(Name = "Doctor Name")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Specialty is required.")]
        public string? Specialty { get; set; } = "ENT";

        public string? Phone { get; set; }
        public string? Email { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Consultation Fee is required.")]
        [Display(Name = "Consultation Fee")]
        public decimal ConsultationFee { get; set; }
        public string Status { get; set; } = "Active";
        public string? PhotoBase64 { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static DoctorInput FromEntity(Doctor d) => new()
        {
            DoctorNo = d.DoctorNo,
            Name = d.Name,
            Specialty = d.Specialty,
            Phone = d.Phone,
            Email = d.Email,
            ConsultationFee = d.ConsultationFee,
            Status = d.Status,
            PhotoBase64 = d.PhotoBase64,
            CreatedBy = d.CreatedBy,
            UpdatedBy = d.UpdatedBy,
            UpdatedAt = d.UpdatedAt
        };

        public Doctor ToEntity(Guid? id)
        {
            var d = new Doctor
            {
                Id = id ?? Guid.Empty,
                DoctorNo = DoctorNo,
                Name = Name.Trim(),
                Specialty = Specialty,
                Phone = Phone,
                Email = Email,
                ConsultationFee = ConsultationFee,
                Status = Status,
                PhotoBase64 = PhotoBase64
            };
            return d;
        }
    }
}
