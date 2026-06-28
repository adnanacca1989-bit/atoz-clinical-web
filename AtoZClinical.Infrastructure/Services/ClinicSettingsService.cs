using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class ClinicSettingsService
{
    private readonly ClinicalDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ClinicRuntimeCache _cache;

    public ClinicSettingsService(
        ClinicalDbContext db,
        UserManager<ApplicationUser> users,
        ClinicRuntimeCache cache)
    {
        _db = db;
        _users = users;
        _cache = cache;
    }

    public Task<ClinicConfiguration> GetOrCreateAsync(Guid clinicId) =>
        _cache.GetOrCreateAsync(ClinicRuntimeCache.ConfigurationKey(clinicId), () => LoadOrCreateConfigAsync(clinicId));

    private async Task<ClinicConfiguration> LoadOrCreateConfigAsync(Guid clinicId)
    {
        var config = await _db.ClinicConfigurations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId);
        if (config is not null) return config;

        config = new ClinicConfiguration { ClinicId = clinicId, AllowDoctorViewAllPatients = true };
        try
        {
            await ClinicSaveHelper.ExecuteIsolatedSaveAsync(_db, () =>
            {
                _db.ClinicConfigurations.Add(config);
                return Task.CompletedTask;
            });
        }
        catch (DbUpdateException)
        {
            config = await _db.ClinicConfigurations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ClinicId == clinicId);
            if (config is null)
                throw;
        }

        return config;
    }

    public Task<ClinicConfiguration?> GetAsync(Guid clinicId) =>
        _db.ClinicConfigurations.FirstOrDefaultAsync(c => c.ClinicId == clinicId);

    public async Task SaveAsync(ClinicConfiguration config)
    {
        config.UpdatedAt = DateTime.UtcNow;
        var exists = await _db.ClinicConfigurations
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Id == config.Id && c.ClinicId == config.ClinicId);

        if (exists)
            _db.ClinicConfigurations.Update(config);
        else
            _db.ClinicConfigurations.Add(config);

        await _db.SaveChangesAsync();
        _cache.InvalidateConfiguration(config.ClinicId);
    }

    public void InvalidateCache(Guid clinicId) =>
        _cache.InvalidateConfiguration(clinicId);

    public Task<List<ApplicationUser>> ListClinicUsersAsync(Guid clinicId) =>
        _users.Users.Where(u => u.ClinicId == clinicId).OrderBy(u => u.UserName).ToListAsync();
}
