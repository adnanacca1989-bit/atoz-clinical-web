using System.Security.Cryptography;
using System.Text;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Infrastructure.Services;

public sealed class PasswordResetService
{
    public const int DefaultExpiryMinutes = 15;
    public const int MaxRequestsPerHour = 5;

    private readonly ClinicalDbContext _db;
    private readonly int _expiryMinutes;

    public PasswordResetService(ClinicalDbContext db, IConfiguration config)
    {
        _db = db;
        _expiryMinutes = config.GetValue("PasswordReset:ExpiryMinutes", DefaultExpiryMinutes);
    }

    /// <summary>Creates a reset token and returns email payload when the account exists.</summary>
    public async Task<PasswordResetEmailPayload?> CreateTokenForEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = email.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == normalized.ToLower(), ct);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
            return null;

        var hourAgo = DateTime.UtcNow.AddHours(-1);
        var recentCount = await _db.PasswordResetTokens
            .CountAsync(t => t.UserId == user.Id && t.CreatedAt >= hourAgo, ct);
        if (recentCount >= MaxRequestsPerHour)
            return null;

        await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && !t.Used && t.ExpiryDate > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Used, true), ct);

        var plainToken = GeneratePlainToken();
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(plainToken),
            ExpiryDate = DateTime.UtcNow.AddMinutes(_expiryMinutes),
            Used = false,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        return new PasswordResetEmailPayload(plainToken, user.Email, user.FullName);
    }

    public async Task<PasswordResetToken?> FindValidTokenAsync(string plainToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plainToken))
            return null;

        var hash = HashToken(plainToken.Trim());
        var row = await _db.PasswordResetTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash && !t.Used && t.ExpiryDate > DateTime.UtcNow, ct);
        return row;
    }

    public async Task MarkUsedAsync(Guid tokenId, CancellationToken ct = default)
    {
        await _db.PasswordResetTokens
            .Where(t => t.Id == tokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Used, true), ct);
    }

    public static string HashToken(string plainToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainToken));
        return Convert.ToHexString(bytes);
    }

    private static string GeneratePlainToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public sealed record PasswordResetEmailPayload(string PlainToken, string Email, string FullName);
