using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Billing;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

public sealed class VendorClinicService
{
    private readonly ClinicalDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly IConfiguration _config;
    private readonly ILogger<VendorClinicService> _logger;

    public VendorClinicService(
        ClinicalDbContext db,
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole> roles,
        IConfiguration config,
        ILogger<VendorClinicService> logger)
    {
        _db = db;
        _users = users;
        _roles = roles;
        _config = config;
        _logger = logger;
    }

    public Task<List<Clinic>> ListClinicsAsync() =>
        _db.Clinics.OrderByDescending(c => c.CreatedAt).ToListAsync();

    public Task<Clinic?> GetClinicAsync(Guid id) =>
        _db.Clinics.FirstOrDefaultAsync(c => c.Id == id);

    public async Task<(Clinic Clinic, ApplicationUser Admin, string PlainPassword)> CreateClinicAsync(CreateClinicRequest request)
    {
        var code = string.IsNullOrWhiteSpace(request.ClinicCode)
            ? await GenerateClinicCodeAsync()
            : request.ClinicCode.Trim().ToUpperInvariant();

        if (await _db.Clinics.AnyAsync(c => c.ClinicCode == code))
        {
            throw new InvalidOperationException($"Clinic code '{code}' already exists.");
        }

        var clinic = new Clinic
        {
            ClinicCode = code,
            Name = request.Name.Trim(),
            ContactPerson = request.ContactPerson?.Trim(),
            Email = request.Email?.Trim(),
            Phone = request.Phone?.Trim(),
            Address = request.Address?.Trim(),
            City = request.City?.Trim(),
            Country = request.Country?.Trim(),
            HostingUrl = request.HostingUrl?.Trim(),
            DatabaseHost = request.DatabaseHost?.Trim(),
            DatabasePort = request.DatabasePort > 0 ? request.DatabasePort : 5432,
            DatabaseName = request.DatabaseName?.Trim(),
            LicenseKey = GenerateLicenseKey(),
            LicenseExpires = ToUtcDate(request.LicenseExpires) ?? DateTime.UtcNow.Date.AddYears(1),
            PlanName = string.IsNullOrWhiteSpace(request.PlanName) ? SubscriptionPlans.Standard : request.PlanName.Trim(),
            MaxUsers = request.MaxUsers > 0 ? request.MaxUsers : 25,
            Status = request.ActivateImmediately ? ClinicStatus.Active : ClinicStatus.Pending,
            Notes = request.Notes?.Trim(),
            EnabledFormKeys = request.EnabledFormKeys,
            SubscriptionStartDate = DateTime.UtcNow.Date
        };

        SaasSubscriptionService.ApplyPlanDefaults(clinic, clinic.PlanName);
        SaasSubscriptionService.SyncSubscriptionDates(clinic, clinic.SubscriptionStartDate, clinic.LicenseExpires);

        if (clinic.PlanName.Equals(SubscriptionPlans.Trial, StringComparison.OrdinalIgnoreCase))
        {
            clinic.TrialEndsAt = clinic.SubscriptionExpiryDate;
            clinic.SubscriptionStatus = SubscriptionStatuses.Trialing;
        }

        _db.Clinics.Add(clinic);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            throw new InvalidOperationException(ex.InnerException?.Message ?? ex.Message, ex);
        }

        var username = request.AdminUsername.Trim();
        if (await _users.FindByNameAsync(username) is not null)
        {
            throw new InvalidOperationException($"Username '{username}' is already taken.");
        }

        var password = string.IsNullOrWhiteSpace(request.AdminPassword)
            ? GeneratePassword()
            : request.AdminPassword;

        var smtpReady = SmtpEmailConfiguration.IsEmailConfigured(_config);
        var requireEmailConfirmation = request.RequireEmailConfirmation
            && smtpReady
            && !string.IsNullOrWhiteSpace(request.Email?.Trim());

        if (request.RequireEmailConfirmation && !string.IsNullOrWhiteSpace(request.Email?.Trim()) && !smtpReady)
        {
            _logger.LogWarning(
                "SMTP not configured — admin user {Username} for clinic {ClinicName} will be auto-confirmed so sign-in is not blocked.",
                username, clinic.Name);
        }

        var admin = new ApplicationUser
        {
            UserName = username,
            Email = request.Email?.Trim(),
            FullName = request.ContactPerson?.Trim() ?? $"{clinic.Name} Admin",
            ClinicId = clinic.Id,
            ClinicRole = ClinicUserRole.ClinicAdmin,
            IsVendorAdmin = false,
            EmailConfirmed = !requireEmailConfirmation
        };

        var result = await _users.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            _db.Clinics.Remove(clinic);
            await _db.SaveChangesAsync();
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await EnsureRoleAsync(ClinicalRoles.ClinicAdmin);
        await _users.AddToRoleAsync(admin, ClinicalRoles.ClinicAdmin);

        await DatabaseInitializer.SeedClinicDefaultsAsync(_db, clinic.Id);

        var config = await _db.ClinicConfigurations.ForClinic(clinic.Id).FirstOrDefaultAsync();
        if (config is not null && !config.PatientPortalEnabled)
        {
            config.PatientPortalEnabled = true;
            await _db.SaveChangesAsync();
        }

        return (clinic, admin, password);
    }

    public Task<List<ApplicationUser>> GetClinicUsersAsync(Guid clinicId) =>
        _users.Users.Where(u => u.ClinicId == clinicId).OrderBy(u => u.UserName).ToListAsync();

    public async Task<(ApplicationUser User, string PlainPassword)> CreateClinicUserAsync(CreateClinicUserRequest request)
    {
        await EnsureCanAddUserAsync(request.ClinicId);

        if (await _users.FindByNameAsync(request.Username.Trim()) is not null)
        {
            throw new InvalidOperationException($"Username '{request.Username}' already exists.");
        }

        if (request.Role == ClinicUserRole.Doctor && request.DoctorRecordId is null)
            throw new InvalidOperationException("Doctor users must be linked to a doctor record.");

        if (request.Role == ClinicUserRole.Doctor && request.DoctorRecordId is Guid doctorId)
        {
            var taken = await _users.Users.AnyAsync(u =>
                u.ClinicId == request.ClinicId && u.DoctorRecordId == doctorId);
            if (taken)
                throw new InvalidOperationException("That doctor is already linked to another user account.");
        }

        var password = string.IsNullOrWhiteSpace(request.Password) ? GeneratePassword() : request.Password;
        var user = new ApplicationUser
        {
            UserName = request.Username.Trim(),
            Email = request.Email?.Trim(),
            FullName = request.FullName.Trim(),
            ClinicId = request.ClinicId,
            ClinicRole = request.Role,
            UserNo = request.UserNo,
            DoctorRecordId = request.Role == ClinicUserRole.Doctor ? request.DoctorRecordId : null,
            EmailConfirmed = true
        };

        var result = await _users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await EnsureRoleAsync(ClinicalRoles.ClinicStaff);
        await _users.AddToRoleAsync(user, ClinicalRoles.ClinicStaff);
        return (user, password);
    }

    public async Task UpdateClinicStatusAsync(Guid clinicId, ClinicStatus status)
    {
        var clinic = await _db.Clinics.FindAsync(clinicId)
            ?? throw new InvalidOperationException("Clinic not found.");
        clinic.Status = status;
        await _db.SaveChangesAsync();
    }

    public async Task ActivateClinicAsync(Guid clinicId, DateTime? licenseExpires = null)
    {
        var clinic = await _db.Clinics.FindAsync(clinicId)
            ?? throw new InvalidOperationException("Clinic not found.");
        clinic.Status = ClinicStatus.Active;
        if (licenseExpires.HasValue)
            clinic.LicenseExpires = ToUtcDate(licenseExpires)!.Value;
        else if (clinic.LicenseExpires is null || clinic.LicenseExpires.Value.Date < DateTime.UtcNow.Date)
            clinic.LicenseExpires = DateTime.UtcNow.Date.AddYears(1);
        await _db.SaveChangesAsync();
    }

    public async Task SuspendClinicAsync(Guid clinicId) =>
        await UpdateClinicStatusAsync(clinicId, ClinicStatus.Suspended);

    public async Task RenewLicenseAsync(Guid clinicId, DateTime licenseExpires, string? planName = null, int? maxUsers = null)
    {
        var clinic = await _db.Clinics.FindAsync(clinicId)
            ?? throw new InvalidOperationException("Clinic not found.");

        if (!string.IsNullOrWhiteSpace(planName))
            SaasSubscriptionService.ApplyPlanDefaults(clinic, planName);

        if (maxUsers is > 0)
            clinic.MaxUsers = maxUsers.Value;

        clinic.Status = ClinicStatus.Active;
        clinic.SubscriptionStatus = clinic.PlanName.Equals(SubscriptionPlans.Trial, StringComparison.OrdinalIgnoreCase)
            ? SubscriptionStatuses.Trialing
            : SubscriptionStatuses.Active;

        SaasSubscriptionService.SyncSubscriptionDates(clinic, DateTime.UtcNow.Date, licenseExpires);
        await _db.SaveChangesAsync();
    }

    public async Task<(Clinic Clinic, ApplicationUser Admin, string PlainPassword)> RegisterTrialClinicAsync(TrialClinicRegistrationRequest request)
    {
        return await CreateClinicAsync(new CreateClinicRequest
        {
            Name = request.ClinicName.Trim(),
            Email = request.Email?.Trim(),
            ContactPerson = request.ClinicName.Trim(),
            AdminUsername = request.AdminUsername.Trim(),
            AdminPassword = request.AdminPassword,
            RequireEmailConfirmation = !string.IsNullOrWhiteSpace(request.Email?.Trim()),
            PlanName = "Trial",
            MaxUsers = 10,
            LicenseExpires = DateTime.UtcNow.Date.AddDays(30),
            ActivateImmediately = true,
            Notes = "30-day self-service trial registration.",
            EnabledFormKeys = ClinicModuleService.SerializeEnabledForms(ClinicalModuleCatalog.AllFormKeys())
        });
    }

    public async Task<(Clinic Clinic, ApplicationUser Admin, string PlainPassword)> RegisterPublicClinicAsync(PublicClinicRegistrationRequest request)
    {
        return await CreateClinicAsync(new CreateClinicRequest
        {
            Name = request.ClinicName,
            ContactPerson = request.ContactPerson,
            Email = request.Email,
            Phone = request.Phone,
            City = request.City,
            Country = request.Country,
            AdminUsername = request.AdminUsername,
            AdminPassword = request.AdminPassword,
            RequireEmailConfirmation = !string.IsNullOrWhiteSpace(request.Email),
            PlanName = "Trial",
            MaxUsers = 10,
            LicenseExpires = DateTime.UtcNow.Date.AddDays(30),
            ActivateImmediately = false,
            Notes = "Self-service registration — pending vendor approval.",
            EnabledFormKeys = ClinicalModuleCatalog.BuildEnabledFormKeysFromGroups(request.EnabledModuleGroups)
        });
    }

    public async Task EnsureCanAddUserAsync(Guid clinicId)
    {
        var clinic = await _db.Clinics.FindAsync(clinicId)
            ?? throw new InvalidOperationException("Clinic not found.");
        var count = await _users.Users.CountAsync(u => u.ClinicId == clinicId);
        if (count >= clinic.MaxUsers)
            throw new InvalidOperationException($"User limit reached ({clinic.MaxUsers}). Upgrade your plan to add more users.");
    }

    public async Task DeleteClinicAsync(Guid clinicId)
    {
        var clinic = await _db.Clinics.FindAsync(clinicId)
            ?? throw new InvalidOperationException("Clinic not found.");

        var users = await _users.Users.Where(u => u.ClinicId == clinicId).ToListAsync();
        foreach (var user in users)
            await _users.DeleteAsync(user);

        _db.Clinics.Remove(clinic);
        await _db.SaveChangesAsync();
    }

    private async Task<string> GenerateClinicCodeAsync()
    {
        var count = await _db.Clinics.CountAsync();
        string code;
        do
        {
            count++;
            code = $"CLN-{count:D4}";
        } while (await _db.Clinics.AnyAsync(c => c.ClinicCode == code));

        return code;
    }

    private static string GenerateLicenseKey() =>
        $"ATZ-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    private static string GeneratePassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        var random = Random.Shared;
        return new string(Enumerable.Range(0, 10).Select(_ => chars[random.Next(chars.Length)]).ToArray()) + "!1";
    }

    private static DateTime? ToUtcDate(DateTime? value)
    {
        if (value is null) return null;
        var date = value.Value.Date;
        return DateTime.SpecifyKind(date, DateTimeKind.Utc);
    }

    private async Task EnsureRoleAsync(string role)
    {
        if (!await _roles.RoleExistsAsync(role))
        {
            await _roles.CreateAsync(new IdentityRole(role));
        }
    }
}

public sealed class CreateClinicRequest
{
    public string Name { get; set; } = string.Empty;
    public string? ClinicCode { get; set; }
    public string? ContactPerson { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? HostingUrl { get; set; }
    public string? DatabaseHost { get; set; }
    public int DatabasePort { get; set; } = 5432;
    public string? DatabaseName { get; set; }
    public DateTime? LicenseExpires { get; set; }
    public string? PlanName { get; set; }
    public int MaxUsers { get; set; } = 25;
    public bool ActivateImmediately { get; set; }
    public string? Notes { get; set; }
    public string? EnabledFormKeys { get; set; }
    public bool RequireEmailConfirmation { get; set; }
    public string AdminUsername { get; set; } = string.Empty;
    public string? AdminPassword { get; set; }
}

public sealed class CreateClinicUserRequest
{
    public Guid ClinicId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Password { get; set; }
    public ClinicUserRole Role { get; set; } = ClinicUserRole.Receptionist;
    public int UserNo { get; set; }
    public Guid? DoctorRecordId { get; set; }
}

public sealed class PublicClinicRegistrationRequest
{
    public string ClinicName { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string AdminUsername { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
    public string[]? EnabledModuleGroups { get; set; }
}

public sealed class TrialClinicRegistrationRequest
{
    public string ClinicName { get; set; } = string.Empty;
    public string AdminUsername { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
