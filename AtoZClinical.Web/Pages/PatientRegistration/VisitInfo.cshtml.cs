using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.PatientRegistration;

public class VisitInfoModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly PatientService _patients;

    public VisitInfoModel(ClinicContextService clinicContext, PatientService patients)
    {
        _clinicContext = clinicContext;
        _patients = patients;
    }

    public async Task<IActionResult> OnGetAsync(string? nationalId, string? phone, string? patientName, Guid? excludeId)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var nextVisit = await _patients.GetNextVisitNumberAsync(
            clinicId.Value, nationalId, phone, excludeId, patientName);
        var existing = await _patients.FindByNationalIdOrPhoneAsync(clinicId.Value, nationalId, phone);

        return new JsonResult(new
        {
            nextVisit,
            isReturning = existing is not null,
            previousVisits = nextVisit - 1,
            lastPatient = existing is null ? null : new
            {
                existing.PatientNo,
                name = existing.FullName,
                existing.Phone,
                existing.City,
                existing.DoctorName,
                existing.Specialty,
                visitNumber = existing.VisitNumber
            }
        });
    }
}
