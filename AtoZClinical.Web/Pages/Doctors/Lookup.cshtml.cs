using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Doctors;

public class LookupModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly DoctorService _doctors;

    public LookupModel(ClinicContextService clinicContext, DoctorService doctors)
    {
        _clinicContext = clinicContext;
        _doctors = doctors;
    }

    public async Task<IActionResult> OnGetAsync(string? search)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var list = await _doctors.ListAsync(clinicId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            list = list.Where(d =>
                d.DoctorNo.ToString().Contains(term) ||
                d.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (d.Specialty?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) ||
                (d.Phone?.Contains(term) == true) ||
                (d.Email?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)).ToList();
        }

        return new JsonResult(list.Select(d => new
        {
            id = d.Id,
            doctorNo = d.DoctorNo,
            name = d.Name,
            specialty = d.Specialty,
            phone = d.Phone,
            email = d.Email,
            consultationFee = d.ConsultationFee,
            status = d.Status
        }));
    }
}
