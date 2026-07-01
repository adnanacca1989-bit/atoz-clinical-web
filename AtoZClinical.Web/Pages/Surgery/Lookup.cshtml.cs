using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Surgery;

public class LookupModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly DoctorSurgeryService _surgeries;

    public LookupModel(ClinicContextService clinicContext, DoctorSurgeryService surgeries)
    {
        _clinicContext = clinicContext;
        _surgeries = surgeries;
    }

    public async Task<IActionResult> OnGetAsync(Guid? patientId)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null || patientId is null) return Forbid();

        var surgery = await _surgeries.GetLatestForPatientAsync(clinicId.Value, patientId.Value);
        if (surgery is null)
            return new JsonResult(new { found = false });

        return new JsonResult(new
        {
            found = true,
            surgeryId = surgery.Id,
            typeOfSurgery = surgery.TypeOfSurgery,
            classify = surgery.Classify,
            surgeryName = surgery.SurgeryName ?? surgery.TypeOfSurgery,
            doctorName = surgery.DoctorName,
            specialty = surgery.Specialty,
            motherName = surgery.MotherName
        });
    }
}
