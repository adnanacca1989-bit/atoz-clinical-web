using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.PatientRegistration;

public class CloneInfoModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly PatientService _patients;

    public CloneInfoModel(ClinicContextService clinicContext, PatientService patients)
    {
        _clinicContext = clinicContext;
        _patients = patients;
    }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var patient = await _patients.GetAsync(clinicId.Value, id);
        if (patient is null) return NotFound();

        var nextNo = await _patients.NextPatientNoAsync(clinicId.Value);
        var nextAppt = await _patients.NextAppointmentIdAsync(clinicId.Value);
        var nextVisit = await _patients.GetNextVisitNumberAsync(
            clinicId.Value, patient.NationalId, patient.Phone, null, patient.FullName);

        return new JsonResult(new
        {
            patientNo = nextNo,
            appointmentId = nextAppt,
            visitNumber = nextVisit.ToString(),
            patientName = patient.FullName,
            gender = patient.Gender,
            dateOfBirth = patient.DateOfBirth?.ToString("yyyy-MM-dd"),
            phone = patient.Phone,
            city = patient.City,
            doctorName = patient.DoctorName,
            specialty = patient.Specialty,
            nationalId = patient.NationalId,
            address = patient.Address,
            bloodGroup = patient.BloodGroup,
            marriedStatus = patient.MarriedStatus,
            motherName = patient.MotherName,
            emergencyContact = patient.EmergencyContact,
            healthInsuranceName = patient.HealthInsuranceName,
            healthInsuranceNumber = patient.HealthInsuranceNumber,
            appointmentDate = DateTime.Today.ToString("yyyy-MM-dd"),
            appointmentTime = DateTime.Now.ToString("HH:mm"),
            status = "Pending"
        });
    }
}
