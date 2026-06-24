using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.ViewComponents;

public sealed class CompanyProfileViewComponent : ViewComponent
{
    private readonly ClinicContextService _clinicContext;
    private readonly ClinicSettingsService _settings;
    private readonly ClinicalDbContext _db;

    public CompanyProfileViewComponent(
        ClinicContextService clinicContext,
        ClinicSettingsService settings,
        ClinicalDbContext db)
    {
        _clinicContext = clinicContext;
        _settings = settings;
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null)
            return Content(string.Empty);

        var clinic = await _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId.Value);
        if (clinic is null)
            return Content(string.Empty);

        var config = await _settings.GetAsync(clinicId.Value);
        var ownerName = config?.OwnerName;
        if (string.IsNullOrWhiteSpace(ownerName))
        {
            ownerName = await _db.ClinicOwners.AsNoTracking()
                .Where(o => o.ClinicId == clinicId.Value && o.IsActive)
                .OrderBy(o => o.OwnerNo)
                .Select(o => o.Name)
                .FirstOrDefaultAsync();
        }
        if (string.IsNullOrWhiteSpace(ownerName))
            ownerName = clinic.ContactPerson;

        return View(new CompanyProfileModel(
            clinic.Name,
            clinic.Address,
            clinic.Email,
            clinic.Phone,
            clinic.Country,
            clinic.City,
            ownerName));
    }
}

public sealed record CompanyProfileModel(
    string Name,
    string? Address,
    string? Email,
    string? Mobile,
    string? Country,
    string? City,
    string? OwnerName);
