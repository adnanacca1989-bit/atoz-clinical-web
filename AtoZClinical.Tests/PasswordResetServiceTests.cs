using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Tests;

public class PasswordResetServiceTests
{
    [Fact]
    public async Task CreateTokenForEmailAsync_returns_payload_for_known_user()
    {
        var clinicId = Guid.NewGuid();
        var tenant = TestClinicProvider.ForClinic(clinicId);
        await using var db = await SqliteTestDatabase.CreateAsync(tenant);

        var userId = Guid.NewGuid().ToString();
        db.Db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = "reset@test.local",
            Email = "reset@test.local",
            FullName = "Reset User",
            ClinicId = clinicId
        });
        await db.Db.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build();
        var service = new PasswordResetService(db.Db, config);

        var payload = await service.CreateTokenForEmailAsync("reset@test.local");

        Assert.NotNull(payload);
        Assert.Equal("reset@test.local", payload!.Email);
        Assert.False(string.IsNullOrWhiteSpace(payload.PlainToken));

        var row = await service.FindValidTokenAsync(payload.PlainToken);
        Assert.NotNull(row);
        Assert.Equal(userId, row!.UserId);
    }

    [Fact]
    public async Task CreateTokenForEmailAsync_returns_null_for_unknown_email()
    {
        var clinicId = Guid.NewGuid();
        var tenant = TestClinicProvider.ForClinic(clinicId);
        await using var db = await SqliteTestDatabase.CreateAsync(tenant);
        var service = new PasswordResetService(db.Db, new ConfigurationBuilder().Build());

        var payload = await service.CreateTokenForEmailAsync("missing@test.local");

        Assert.Null(payload);
    }

    [Fact]
    public async Task CreateTokenForEmailAsync_rate_limits_after_max_requests()
    {
        var clinicId = Guid.NewGuid();
        var tenant = TestClinicProvider.ForClinic(clinicId);
        await using var db = await SqliteTestDatabase.CreateAsync(tenant);

        var userId = Guid.NewGuid().ToString();
        db.Db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = "limit@test.local",
            Email = "limit@test.local",
            FullName = "Limit User",
            ClinicId = clinicId
        });
        await db.Db.SaveChangesAsync();

        var service = new PasswordResetService(db.Db, new ConfigurationBuilder().Build());

        for (var i = 0; i < PasswordResetService.MaxRequestsPerHour; i++)
        {
            var payload = await service.CreateTokenForEmailAsync("limit@test.local");
            Assert.NotNull(payload);
        }

        var blocked = await service.CreateTokenForEmailAsync("limit@test.local");
        Assert.Null(blocked);
    }

    [Fact]
    public async Task MarkUsedAsync_invalidates_token()
    {
        var clinicId = Guid.NewGuid();
        var tenant = TestClinicProvider.ForClinic(clinicId);
        await using var db = await SqliteTestDatabase.CreateAsync(tenant);

        db.Db.Users.Add(new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "used@test.local",
            Email = "used@test.local",
            FullName = "Used User",
            ClinicId = clinicId
        });
        await db.Db.SaveChangesAsync();

        var service = new PasswordResetService(db.Db, new ConfigurationBuilder().Build());
        var payload = await service.CreateTokenForEmailAsync("used@test.local");
        Assert.NotNull(payload);

        var row = await service.FindValidTokenAsync(payload!.PlainToken);
        Assert.NotNull(row);

        await service.MarkUsedAsync(row!.Id);

        var after = await service.FindValidTokenAsync(payload.PlainToken);
        Assert.Null(after);
    }
}
