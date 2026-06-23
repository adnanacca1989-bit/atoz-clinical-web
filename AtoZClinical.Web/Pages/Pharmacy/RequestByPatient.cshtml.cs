using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Pharmacy;

public class RequestByPatientModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly PharmacyRequestService _requests;

    public RequestByPatientModel(ClinicContextService clinicContext, PharmacyRequestService requests)
    {
        _clinicContext = clinicContext;
        _requests = requests;
    }

    public async Task<IActionResult> OnGetAsync(string? patientName, string? patientId)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        if (string.IsNullOrWhiteSpace(patientName) && string.IsNullOrWhiteSpace(patientId))
            return new JsonResult(null);

        var request = await _requests.GetLatestByPatientAsync(clinicId.Value, patientName, patientId);
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
                barcode = l.Barcode,
                medicineCode = l.MedicineCode,
                medicineName = l.MedicineName,
                dosage = l.Dosage,
                uom = l.Uom,
                qty = l.Qty,
                unitPrice = l.UnitPrice,
                total = l.Total
            })
        });
    }
}
