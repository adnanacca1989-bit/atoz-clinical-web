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



    public async Task<IActionResult> OnGetAsync(string? search)

    {

        var clinicId = await _clinicContext.GetClinicIdAsync();

        if (clinicId is null) return Forbid();



        var list = await _patients.ListAsync(clinicId.Value, search);

        if (!string.IsNullOrWhiteSpace(search))

        {

            var term = search.Trim();

            list = list.Where(p =>

                p.PatientNo.Contains(term, StringComparison.OrdinalIgnoreCase) ||

                p.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||

                (p.Phone?.Contains(term) == true) ||

                (p.City?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) ||

                (p.DoctorName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) ||

                (p.Specialty?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) ||

                (p.NationalId?.Contains(term) == true)).ToList();

        }



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
            motherName = p.MotherName,

            status = p.Status

        }));

    }

}


