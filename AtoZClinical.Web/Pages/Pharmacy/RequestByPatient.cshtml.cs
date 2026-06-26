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
    private readonly PharmacyItemRegistrationService _items;

    public RequestByPatientModel(
        ClinicContextService clinicContext,
        PharmacyRequestService requests,
        PharmacyItemRegistrationService items)
    {
        _clinicContext = clinicContext;
        _requests = requests;
        _items = items;
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

        var registeredItems = await _items.ListActiveAsync(clinicId.Value);

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
                .Where(l => l.Qty > 0 && (!string.IsNullOrWhiteSpace(l.MedicineName) || !string.IsNullOrWhiteSpace(l.MedicineCode) || !string.IsNullOrWhiteSpace(l.Barcode)))
                .OrderBy(l => l.LineNo)
                .Select(l =>
                {
                    var registered = PharmacyItemRegistrationService.ResolveForLine(
                        registeredItems, l.Barcode, l.MedicineCode, l.MedicineName);
                    var defaultPrice = registered?.DefaultUnitPrice ?? l.UnitPrice;

                    return new
                    {
                        lineNo = l.LineNo,
                        itemId = registered?.Id,
                        barcode = registered?.Barcode ?? l.Barcode,
                        medicineCode = registered?.MedicineCode ?? l.MedicineCode,
                        medicineName = registered?.MedicineName ?? l.MedicineName,
                        dosage = registered?.Dosage ?? l.Dosage,
                        uom = registered?.BaseUom ?? l.Uom,
                        qty = l.Qty,
                        unitPrice = defaultPrice,
                        defaultUnitPrice = defaultPrice,
                        total = l.Qty * defaultPrice
                    };
                })
        });
    }
}
