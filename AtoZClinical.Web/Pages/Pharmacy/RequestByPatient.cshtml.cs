using AtoZClinical.Core.Entities;
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

    public async Task<IActionResult> OnGetAsync(string? patientName, string? patientId, int? requestNo)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        PharmacyRequest? request = null;
        if (requestNo.HasValue && requestNo.Value > 0)
            request = await _requests.GetByRequestNoAsync(clinicId.Value, requestNo.Value);
        else if (!string.IsNullOrWhiteSpace(patientName) || !string.IsNullOrWhiteSpace(patientId))
            request = await _requests.GetLatestByPatientAsync(clinicId.Value, patientName, patientId);

        if (request is null) return new JsonResult(null);

        return new JsonResult(new
        {
            requestNo = request.RequestNo,
            requestDate = request.RequestDate.ToString("yyyy-MM-dd"),
            patientName = request.PatientName,
            patientId = request.PatientId,
            age = request.Age,
            gender = request.Gender,
            phone = request.Phone,
            city = request.City,
            doctorName = request.DoctorName,
            specialty = request.Specialty,
            lines = request.Lines
                .Where(l => l.Qty > 0 && (!string.IsNullOrWhiteSpace(l.MedicineName) || !string.IsNullOrWhiteSpace(l.MedicineCode)))
                .OrderBy(l => l.LineNo)
                .Select(l => new
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
