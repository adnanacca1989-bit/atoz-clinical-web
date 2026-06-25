using System.Security.Cryptography;
using System.Text;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class ClinicApiKeyService
{
    private readonly ClinicalDbContext _db;

    public ClinicApiKeyService(ClinicalDbContext db) => _db = db;

    public Task<List<ClinicApiKey>> ListAsync(Guid clinicId) =>
        _db.ClinicApiKeys
            .Where(k => k.ClinicId == clinicId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();

    public async Task<(ClinicApiKey Record, string PlainKey)> CreateAsync(Guid clinicId, string name)
    {
        var plainKey = GeneratePlainKey();
        var record = new ClinicApiKey
        {
            ClinicId = clinicId,
            Name = name.Trim(),
            KeyPrefix = plainKey[..12],
            KeyHash = HashKey(plainKey)
        };
        _db.ClinicApiKeys.Add(record);
        await _db.SaveChangesAsync();
        return (record, plainKey);
    }

    public async Task<bool> RevokeAsync(Guid clinicId, Guid keyId)
    {
        var key = await _db.ClinicApiKeys.FirstOrDefaultAsync(k => k.ClinicId == clinicId && k.Id == keyId);
        if (key is null) return false;
        key.IsActive = false;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<Guid?> ValidateAsync(string plainKey)
    {
        if (string.IsNullOrWhiteSpace(plainKey) || plainKey.Length < 20)
            return null;

        var prefix = plainKey.Length >= 12 ? plainKey[..12] : plainKey;
        var hash = HashKey(plainKey);
        var key = await _db.ClinicApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.IsActive && k.KeyPrefix == prefix && k.KeyHash == hash);

        if (key is null) return null;

        await _db.ClinicApiKeys
            .Where(k => k.Id == key.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, DateTime.UtcNow));

        return key.ClinicId;
    }

    private static string GeneratePlainKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return "atz_" + Convert.ToBase64String(bytes)
            .Replace('+', 'x')
            .Replace('/', 'y')
            .TrimEnd('=');
    }

    public static string HashKey(string plainKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plainKey));
        return Convert.ToHexString(hash);
    }
}
