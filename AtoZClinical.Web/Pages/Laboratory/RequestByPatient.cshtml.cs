using AtoZClinical.Infrastructure.Services;

using AtoZClinical.Web.Services;

using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Mvc.RazorPages;



namespace AtoZClinical.Web.Pages.Laboratory;



public class RequestByPatientModel : PageModel

{

    private readonly ClinicContextService _clinicContext;

    private readonly LabRequestService _requests;



    public RequestByPatientModel(ClinicContextService clinicContext, LabRequestService requests)

    {

        _clinicContext = clinicContext;

        _requests = requests;

    }



    public async Task<IActionResult> OnGetAsync(string? patientName, string? patientBarcode)

    {

        var clinicId = await _clinicContext.GetClinicIdAsync();

        if (clinicId is null) return Forbid();



        if (string.IsNullOrWhiteSpace(patientName) && string.IsNullOrWhiteSpace(patientBarcode))

            return new JsonResult(null);



        var request = await _requests.GetLatestByPatientAsync(clinicId.Value, patientName, patientBarcode);

        if (request is null) return new JsonResult(null);



        return new JsonResult(new

        {

            requestNo = request.RequestNo,

            requestDate = request.RequestDate.ToString("yyyy-MM-dd"),

            patientName = request.PatientName,

            doctorName = request.DoctorName,

            specialty = request.Specialty,

            lines = request.Lines.OrderBy(l => l.LineNo).Select(l => new

            {

                lineNo = l.LineNo,

                testCode = l.TestCode,

                testName = l.TestName,

                category = l.Category

            })

        });

    }

}


