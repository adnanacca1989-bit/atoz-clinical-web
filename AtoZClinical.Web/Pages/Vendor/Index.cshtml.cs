using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Vendor;

public class IndexModel : PageModel
{
    private readonly VendorClinicService _vendor;

    public IndexModel(VendorClinicService vendor) => _vendor = vendor;

    public List<Clinic> Clinics { get; private set; } = [];

    public async Task OnGetAsync() => Clinics = await _vendor.ListClinicsAsync();
}
