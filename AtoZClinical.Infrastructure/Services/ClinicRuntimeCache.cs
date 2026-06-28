using Microsoft.Extensions.Caching.Memory;

namespace AtoZClinical.Infrastructure.Services;

public sealed class ClinicRuntimeCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly string[] KnownRoles =
    [
        "Admin", "Doctor", "Reception", "Lab", "Radiology", "Cashier",
        "Accountant", "Pharmacist", "Lab Technician", "Radiology Technician", "Nurse"
    ];

    private readonly IMemoryCache _cache;

    public ClinicRuntimeCache(IMemoryCache cache) => _cache = cache;

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory)
    {
        return (await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return await factory();
        }))!;
    }

    public bool TryGet<T>(string key, out T? value) => _cache.TryGetValue(key, out value);

    public void SetWithTtl<T>(string key, T value)
    {
        _cache.Set(key, value, Ttl);
    }

    public void InvalidateConfiguration(Guid clinicId) =>
        _cache.Remove(ConfigurationKey(clinicId));

    public void InvalidateEnabledForms(Guid clinicId) =>
        _cache.Remove(EnabledFormsKey(clinicId));

    public void InvalidateVisibleForms(Guid clinicId, string? roleName = null)
    {
        if (!string.IsNullOrWhiteSpace(roleName))
        {
            _cache.Remove(VisibleFormsKey(clinicId, roleName));
            return;
        }

        foreach (var role in KnownRoles)
            _cache.Remove(VisibleFormsKey(clinicId, role));
    }

    public void InvalidateClinic(Guid clinicId)
    {
        InvalidateConfiguration(clinicId);
        InvalidateEnabledForms(clinicId);
        InvalidateVisibleForms(clinicId);
    }

    public static string ConfigurationKey(Guid clinicId) => $"clinic:config:{clinicId}";
    public static string EnabledFormsKey(Guid clinicId) => $"clinic:enabled-forms:{clinicId}";
    public static string VisibleFormsKey(Guid clinicId, string roleName) =>
        $"clinic:visible-forms:{clinicId}:{roleName}";
}
