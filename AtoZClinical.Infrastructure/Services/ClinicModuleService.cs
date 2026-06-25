using System.Text.Json;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class ClinicModuleService
{
    public const string EnabledFormsItemKey = "ClinicEnabledFormKeys";

    private readonly ClinicalDbContext _db;
    private readonly ClinicRuntimeCache _cache;

    public ClinicModuleService(ClinicalDbContext db, ClinicRuntimeCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public Task<HashSet<string>> GetEnabledFormsAsync(Guid clinicId) =>
        _cache.GetOrCreateAsync(ClinicRuntimeCache.EnabledFormsKey(clinicId), async () =>
        {
            var clinic = await _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId);
            return ParseEnabledForms(clinic);
        });

    public static HashSet<string> ParseEnabledForms(Clinic? clinic)
    {
        if (clinic is null || string.IsNullOrWhiteSpace(clinic.EnabledFormKeys))
            return ClinicalModuleCatalog.AllFormKeys();

        try
        {
            var keys = JsonSerializer.Deserialize<string[]>(clinic.EnabledFormKeys);
            if (keys is null || keys.Length == 0)
                return ClinicalModuleCatalog.AllFormKeys();
            return keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return ClinicalModuleCatalog.AllFormKeys();
        }
    }

    public static string SerializeEnabledForms(IEnumerable<string> formKeys) =>
        JsonSerializer.Serialize(formKeys.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToArray());
}
