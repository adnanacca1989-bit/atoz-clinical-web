using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class ClinicLookupService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;

    public ClinicLookupService(ClinicalDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public Task<List<ClinicUom>> ListUomsAsync(Guid clinicId) =>
        _db.ClinicUoms.Where(x => x.ClinicId == clinicId).OrderBy(x => x.UomNo).ToListAsync();

    public Task<ClinicUom?> GetUomAsync(Guid clinicId, Guid id) =>
        _db.ClinicUoms.FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Id == id);

    public async Task<ClinicUom> SaveUomAsync(Guid clinicId, ClinicUom item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        item.Code = item.Code.Trim();
        item.Name = string.IsNullOrWhiteSpace(item.Name) ? item.Code : item.Name.Trim();

        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.UomNo = (await _db.ClinicUoms.Where(x => x.ClinicId == clinicId).MaxAsync(x => (int?)x.UomNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.ClinicUoms.Add(item);
        }
        else
            _db.ClinicUoms.Update(item);

        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Define UOM", isNew ? "Create" : "Update", $"{item.Code} — {item.Name}");
        return item;
    }

    public async Task DeleteUomAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetUomAsync(clinicId, id);
        if (item is null) return;
        _db.ClinicUoms.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Define UOM", "Delete", $"{item.Code}");
    }

    public Task<List<ClinicCurrency>> ListCurrenciesAsync(Guid clinicId) =>
        _db.ClinicCurrencies.Where(x => x.ClinicId == clinicId).OrderBy(x => x.CurrencyNo).ToListAsync();

    public Task<ClinicCurrency?> GetCurrencyAsync(Guid clinicId, Guid id) =>
        _db.ClinicCurrencies.FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Id == id);

    public async Task<ClinicCurrency> SaveCurrencyAsync(Guid clinicId, ClinicCurrency item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        item.Code = item.Code.Trim().ToUpperInvariant();
        item.Symbol = item.Symbol.Trim();
        item.Name = item.Name.Trim();

        if (item.IsDefault)
        {
            var others = await _db.ClinicCurrencies.Where(x => x.ClinicId == clinicId && x.Id != item.Id).ToListAsync();
            foreach (var o in others) o.IsDefault = false;
        }

        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.CurrencyNo = (await _db.ClinicCurrencies.Where(x => x.ClinicId == clinicId).MaxAsync(x => (int?)x.CurrencyNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.ClinicCurrencies.Add(item);
        }
        else
            _db.ClinicCurrencies.Update(item);

        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Define Currency", isNew ? "Create" : "Update", $"{item.Code} — {item.Name}");
        return item;
    }

    public async Task DeleteCurrencyAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetCurrencyAsync(clinicId, id);
        if (item is null) return;
        _db.ClinicCurrencies.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Define Currency", "Delete", item.Code);
    }

    public Task<List<ClinicOwner>> ListOwnersAsync(Guid clinicId) =>
        _db.ClinicOwners.Where(x => x.ClinicId == clinicId).OrderBy(x => x.OwnerNo).ToListAsync();

    public Task<ClinicOwner?> GetOwnerAsync(Guid clinicId, Guid id) =>
        _db.ClinicOwners.FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Id == id);

    public async Task<ClinicOwner> SaveOwnerAsync(Guid clinicId, ClinicOwner item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        item.Name = item.Name.Trim();

        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.OwnerNo = (await _db.ClinicOwners.Where(x => x.ClinicId == clinicId).MaxAsync(x => (int?)x.OwnerNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.ClinicOwners.Add(item);
        }
        else
            _db.ClinicOwners.Update(item);

        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Define Owner", isNew ? "Create" : "Update", item.Name);
        return item;
    }

    public async Task DeleteOwnerAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetOwnerAsync(clinicId, id);
        if (item is null) return;
        _db.ClinicOwners.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Define Owner", "Delete", item.Name);
    }

    public Task<List<ClinicLanguage>> ListLanguagesAsync(Guid clinicId) =>
        _db.ClinicLanguages.Where(x => x.ClinicId == clinicId).OrderBy(x => x.LanguageNo).ToListAsync();

    public Task<ClinicLanguage?> GetLanguageAsync(Guid clinicId, Guid id) =>
        _db.ClinicLanguages.FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Id == id);

    public async Task<ClinicLanguage> SaveLanguageAsync(Guid clinicId, ClinicLanguage item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        item.Code = item.Code.Trim().ToLowerInvariant();
        item.Name = item.Name.Trim();

        if (item.IsDefault)
        {
            var others = await _db.ClinicLanguages.Where(x => x.ClinicId == clinicId && x.Id != item.Id).ToListAsync();
            foreach (var o in others) o.IsDefault = false;
        }

        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.LanguageNo = (await _db.ClinicLanguages.Where(x => x.ClinicId == clinicId).MaxAsync(x => (int?)x.LanguageNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.ClinicLanguages.Add(item);
        }
        else
            _db.ClinicLanguages.Update(item);

        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Define Language", isNew ? "Create" : "Update", item.Name);
        return item;
    }

    public async Task DeleteLanguageAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetLanguageAsync(clinicId, id);
        if (item is null) return;
        _db.ClinicLanguages.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Define Language", "Delete", item.Name);
    }

    public Task<List<ClinicVendor>> ListVendorsAsync(Guid clinicId, bool activeOnly = false)
    {
        var q = _db.ClinicVendors.Where(x => x.ClinicId == clinicId);
        if (activeOnly) q = q.Where(x => x.IsActive);
        return q.OrderBy(x => x.VendorNo).ToListAsync();
    }

    public Task<ClinicVendor?> GetVendorAsync(Guid clinicId, Guid id) =>
        _db.ClinicVendors.FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Id == id);

    public async Task<ClinicVendor> SaveVendorAsync(Guid clinicId, ClinicVendor item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        item.Name = item.Name.Trim();

        if (isNew)
        {
            var template = item;
            item = await ClinicSaveHelper.InsertWithSequenceRetryAsync(
                _db,
                async _ =>
                {
                    var nextNo = (await _db.ClinicVendors.Where(x => x.ClinicId == clinicId).MaxAsync(x => (int?)x.VendorNo) ?? 0) + 1;
                    return new ClinicVendor
                    {
                        Id = Guid.NewGuid(),
                        ClinicId = clinicId,
                        VendorNo = nextNo,
                        Name = template.Name,
                        Phone = template.Phone,
                        Email = template.Email,
                        Address = template.Address,
                        IsActive = template.IsActive,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                },
                row => _db.ClinicVendors.Add(row),
                ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_ClinicVendors_ClinicId_VendorNo"),
                failureMessage: "Could not save vendor");
        }
        else
        {
            var existing = await GetVendorAsync(clinicId, item.Id);
            if (existing is null)
                throw new InvalidOperationException("Vendor record was not found.");
            item.VendorNo = existing.VendorNo;
            item.CreatedAt = existing.CreatedAt;
            _db.ClinicVendors.Update(item);
            await _db.SaveChangesAsync();
        }

        await _audit.LogAsync(clinicId, userName, "Define Vendor", isNew ? "Create" : "Update", item.Name);
        return item;
    }

    public async Task DeleteVendorAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetVendorAsync(clinicId, id);
        if (item is null) return;
        _db.ClinicVendors.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Define Vendor", "Delete", item.Name);
    }
}
