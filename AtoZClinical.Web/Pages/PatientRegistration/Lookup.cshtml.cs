using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.PatientRegistration;

public class LookupModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly PatientService _patients;

    public LookupModel(ClinicContextService clinicContext, PatientService patients)
    {
        _clinicContext = clinicContext;
        _patients = patients;
    }

    public async Task<IActionResult> OnGetAsync(
        string? search,
        DateTime? fromDate,
        DateTime? toDate,
        string? status,
        string? sortBy)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var list = await _patients.ListForPickerAsync(clinicId.Value, search, fromDate, toDate, status, sortBy);

        return new JsonResult(list.Select(p => new
        {
            id = p.Id,
            patientNo = p.PatientNo,
            name = p.FullName,
            gender = p.Gender,
            age = p.AgeYears,
            phone = p.Phone,
            city = p.City,
            doctorName = p.DoctorName,
            specialty = p.Specialty,
            appointmentDate = p.AppointmentDate?.ToString("M/d/yyyy"),
            appointmentTime = p.AppointmentTime.HasValue
                ? DateTime.Today.Add(p.AppointmentTime.Value).ToString("h:mm tt")
                : null,
            dateOfBirth = p.DateOfBirth?.ToString("yyyy-MM-dd"),
            nationalId = p.NationalId,
            motherName = p.MotherName,
            status = p.Status
        }));
    }
}
