using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>Safe user lookup that tolerates duplicate email/username rows in AspNetUsers.</summary>
public sealed class ApplicationUserLookup
{
    private readonly ClinicalDbContext _db;
    private readonly ILogger<ApplicationUserLookup> _logger;

    public ApplicationUserLookup(ClinicalDbContext db, ILogger<ApplicationUserLookup> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApplicationUser?> FindByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken ct = default)
    {
        var input = usernameOrEmail.Trim();
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var normalizedName = input.ToUpperInvariant();
        var byName = await _db.Users
            .AsNoTracking()
            .Where(u => u.NormalizedUserName == normalizedName)
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (byName is not null)
        {
            await LogDuplicatesIfAnyAsync(u => u.NormalizedUserName == normalizedName, "username", input, ct);
            return byName;
        }

        var byEmail = await _db.Users
            .AsNoTracking()
            .Where(u => u.NormalizedEmail == normalizedName)
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (byEmail is not null)
            await LogDuplicatesIfAnyAsync(u => u.NormalizedEmail == normalizedName, "email", input, ct);

        return byEmail;
    }

    private async Task LogDuplicatesIfAnyAsync(
        System.Linq.Expressions.Expression<Func<ApplicationUser, bool>> predicate,
        string field,
        string value,
        CancellationToken ct)
    {
        var count = await _db.Users.AsNoTracking().CountAsync(predicate, ct);
        if (count > 1)
        {
            _logger.LogWarning(
                "Multiple user accounts share the same {Field} ({Value}). Using the oldest account.",
                field,
                value);
        }
    }
}
