using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Pharmacy;

public class ItemLookupModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly PharmacyInventoryService _inventory;

    public ItemLookupModel(ClinicContextService clinicContext, PharmacyInventoryService inventory)
    {
        _clinicContext = clinicContext;
        _inventory = inventory;
    }

    public async Task<IActionResult> OnGetAsync(string barcode)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();
        if (string.IsNullOrWhiteSpace(barcode))
            return new JsonResult(new { found = false });

        var item = await _inventory.LookupByBarcodeAsync(clinicId.Value, barcode.Trim());
        if (item is null)
            return new JsonResult(new { found = false });

        return new JsonResult(new
        {
            found = true,
            item.Barcode,
            medicineCode = item.MedicineCode,
            medicineName = item.MedicineName,
            dosage = item.Dosage,
            baseUom = item.BaseUom,
            alternateUom = item.AlternateUom,
            conversionFactor = item.ConversionFactor,
            defaultUnitPrice = item.DefaultUnitPrice,
            qtyOnHand = item.QuantityOnHand,
            movingAverageCost = item.MovingAverageCost
        });
    }
}
