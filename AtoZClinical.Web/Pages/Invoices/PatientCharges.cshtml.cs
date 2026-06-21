using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Invoices;

public class PatientChargesModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly PatientInvoiceService _charges;

    public PatientChargesModel(ClinicContextService clinicContext, PatientInvoiceService charges)
    {
        _clinicContext = clinicContext;
        _charges = charges;
    }

    public async Task<IActionResult> OnGetAsync(string? patientBarcode, string? patientName)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var summary = await _charges.GetChargesAsync(clinicId.Value, patientBarcode, patientName);
        return new JsonResult(new
        {
            lines = summary.Lines.Select(l => new
            {
                serviceName = l.ServiceName,
                qty = l.Qty,
                unitFee = l.UnitFee,
                category = l.Category
            }),
            subTotal = summary.SubTotal,
            totalPaid = summary.TotalPaid,
            balance = summary.Balance
        });
    }
}
