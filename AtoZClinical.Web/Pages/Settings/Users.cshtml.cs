using AtoZClinical.Core.Enums;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Web.Pages.Settings;

public class UsersModel : SettingsFormPageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly VendorClinicService _vendor;
    private readonly DoctorService _doctors;
    private readonly AuditService _audit;
    private readonly ILogger<UsersModel> _logger;

    public UsersModel(
        ClinicContextService clinicContext,
        UserManager<ApplicationUser> users,
        VendorClinicService vendor,
        DoctorService doctors,
        AuditService audit,
        ILogger<UsersModel> logger)
        : base(clinicContext)
    {
        _users = users;
        _vendor = vendor;
        _doctors = doctors;
        _audit = audit;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? UserRecordId { get; set; }

    [BindProperty] public UserInput Input { get; set; } = new();
    public List<ApplicationUser> Records { get; private set; } = [];
    public List<Doctor> DoctorOptions { get; private set; } = [];
    public bool IsNewUser => string.IsNullOrEmpty(UserRecordId);

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireSettingsClinicIdAsync();
        if (clinicId is null) return ClinicRequired();
        await LoadAsync(clinicId.Value);
        if (!string.IsNullOrEmpty(UserRecordId) && !NewRecord)
            await LoadRecord(clinicId.Value, UserRecordId);
        else if (NewRecord)
            await PrepareNew(clinicId.Value);
        else if (Records.Count > 0)
            await LoadRecord(clinicId.Value, Records[0].Id);
        else
            await PrepareNew(clinicId.Value);
        SetFormViewData("Define User", null, null, null);
        return Page();
    }

    protected override Task<IActionResult> SaveSettingsCoreAsync() => SaveCoreAsync();
    public Task<IActionResult> OnPostNewAsync() => Task.FromResult<IActionResult>(RedirectToPage(new { Search, NewRecord = true, UserRecordId = (string?)null }));
    public Task<IActionResult> OnPostClearAsync() => Task.FromResult<IActionResult>(RedirectToPage(new { Search, NewRecord = true, UserRecordId = (string?)null }));
    public Task<IActionResult> OnPostDeleteAsync() => DeleteCoreAsync();
    public Task<IActionResult> OnPostBackAsync() => NavigateCoreAsync(-1);
    public Task<IActionResult> OnPostNextAsync() => NavigateCoreAsync(1);

    private async Task LoadAsync(Guid clinicId)
    {
        Records = await _users.Users.Where(u => u.ClinicId == clinicId).OrderBy(u => u.UserName).ToListAsync();
        DoctorOptions = await _doctors.ListAsync(clinicId);
        if (!string.IsNullOrWhiteSpace(Search))
            Records = Records.Where(u =>
                (u.UserName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                (u.FullName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, string id)
    {
        var user = await _users.Users.FirstOrDefaultAsync(u => u.Id == id && u.ClinicId == clinicId);
        if (user is null) return;
        UserRecordId = user.Id;
        Input = UserInput.FromEntity(user);
        await SyncDoctorDisplayAsync(clinicId);
    }

    private async Task PrepareNew(Guid clinicId)
    {
        UserRecordId = null;
        var clinicUsers = await _users.Users.Where(u => u.ClinicId == clinicId).ToListAsync();
        var nextNo = clinicUsers.Count > 0 ? clinicUsers.Max(u => u.UserNo) + 1 : 1;
        Input = new UserInput
        {
            UserNo = nextNo,
            IsActive = true,
            Role = ClinicUserRole.Receptionist
        };
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireSettingsClinicIdAsync();
        if (clinicId is null) return ClinicRequired();
        var actor = User.Identity?.Name;

        if (IsNewUser)
        {
            if (string.IsNullOrWhiteSpace(Input.Username))
            {
                ModelState.AddModelError(string.Empty, "Username is required.");
                await LoadAsync(clinicId.Value);
                return Page();
            }

            var (doctorError, resolvedDoctor) = await ValidateDoctorUserAsync(clinicId.Value, Input);
            if (doctorError is not null)
            {
                ModelState.AddModelError(string.Empty, doctorError);
                await LoadAsync(clinicId.Value);
                return Page();
            }

            if (Input.Role != ClinicUserRole.Doctor && string.IsNullOrWhiteSpace(Input.FullName))
            {
                ModelState.AddModelError(string.Empty, "Full Name is required.");
                await LoadAsync(clinicId.Value);
                return Page();
            }

            var fullName = Input.Role == ClinicUserRole.Doctor
                ? resolvedDoctor!.Name
                : Input.FullName.Trim();

            try
            {
                var (_, _) = await _vendor.CreateClinicUserAsync(new CreateClinicUserRequest
                {
                    ClinicId = clinicId.Value,
                    UserNo = Input.UserNo,
                    Username = Input.Username,
                    FullName = fullName,
                    Email = Input.Email,
                    Password = Input.Password,
                    Role = Input.Role,
                    DoctorRecordId = Input.Role == ClinicUserRole.Doctor ? Input.DoctorRecordId : null
                });

                if (Input.Role == ClinicUserRole.Doctor && Input.DoctorRecordId.HasValue)
                {
                    await _audit.LogAsync(clinicId.Value, actor, "Define User", "Doctor Link",
                        $"Linked new user {Input.Username} to doctor {resolvedDoctor!.Name} ({Input.DoctorRecordId})");
                }

                var created = await _users.FindByNameAsync(Input.Username);
                if (SaveMode == "New")
                    return RedirectToPage(new { Search, NewRecord = true });
                return RedirectToPage(new { UserRecordId = created?.Id, Search });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await LoadAsync(clinicId.Value);
                return Page();
            }
        }

        var user = await _users.FindByIdAsync(UserRecordId!);
        if (user is null || user.ClinicId != clinicId) return RedirectToPage();

        var previousRole = user.ClinicRole;
        var previousDoctorId = user.DoctorRecordId;

        var (editDoctorError, editResolvedDoctor) = await ValidateDoctorUserAsync(clinicId.Value, Input, user.Id);
        if (editDoctorError is not null)
        {
            ModelState.AddModelError(string.Empty, editDoctorError);
            await LoadAsync(clinicId.Value);
            await LoadRecord(clinicId.Value, UserRecordId!);
            return Page();
        }

        if (Input.Role != ClinicUserRole.Doctor && string.IsNullOrWhiteSpace(Input.FullName))
        {
            ModelState.AddModelError(string.Empty, "Full Name is required.");
            await LoadAsync(clinicId.Value);
            await LoadRecord(clinicId.Value, UserRecordId!);
            return Page();
        }

        user.Email = Input.Email?.Trim();
        user.ClinicRole = Input.Role;
        user.IsActive = Input.IsActive;
        user.DoctorRecordId = Input.Role == ClinicUserRole.Doctor ? Input.DoctorRecordId : null;
        user.FullName = Input.Role == ClinicUserRole.Doctor
            ? editResolvedDoctor!.Name
            : Input.FullName.Trim();

        await _users.UpdateAsync(user);
        await LogUserChangesAsync(clinicId.Value, actor, user.UserName, previousRole, previousDoctorId, user, editResolvedDoctor);

        if (!string.IsNullOrWhiteSpace(Input.Password))
        {
            await _users.RemovePasswordAsync(user);
            await _users.AddPasswordAsync(user, Input.Password);
        }
        if (SaveMode == "New")
            return RedirectToPage(new { Search, NewRecord = true });
        return RedirectToPage(new { UserRecordId = user.Id, Search });
    }

    private async Task LogUserChangesAsync(
        Guid clinicId,
        string? actor,
        string? username,
        ClinicUserRole? previousRole,
        Guid? previousDoctorId,
        ApplicationUser user,
        Doctor? doctor)
    {
        if (previousRole != user.ClinicRole)
        {
            await _audit.LogAsync(clinicId, actor, "Define User", "Role Change",
                $"User {username}: {previousRole} → {user.ClinicRole}");
        }

        if (user.ClinicRole == ClinicUserRole.Doctor)
        {
            if (previousDoctorId != user.DoctorRecordId && user.DoctorRecordId.HasValue)
            {
                var action = previousDoctorId.HasValue ? "Doctor Change" : "Doctor Link";
                await _audit.LogAsync(clinicId, actor, "Define User", action,
                    $"User {username} linked to doctor {doctor?.Name} ({user.DoctorRecordId})");
            }
        }
        else if (previousDoctorId.HasValue)
        {
            await _audit.LogAsync(clinicId, actor, "Define User", "Doctor Unlink",
                $"User {username} doctor link cleared (was {previousDoctorId})");
        }
    }

    private async Task<IActionResult> DeleteCoreAsync()
    {
        var clinicId = await RequireSettingsClinicIdAsync();
        if (clinicId is null || string.IsNullOrEmpty(UserRecordId)) return RedirectToPage();
        var user = await _users.FindByIdAsync(UserRecordId);
        if (user is null || user.ClinicId != clinicId) return RedirectToPage();
        if (user.UserName == UserName)
        {
            ModelState.AddModelError(string.Empty, "You cannot delete your own account.");
            await LoadAsync(clinicId.Value);
            await LoadRecord(clinicId.Value, UserRecordId);
            return Page();
        }
        await _users.DeleteAsync(user);
        return RedirectToPage();
    }

    private async Task<IActionResult> NavigateCoreAsync(int delta)
    {
        var clinicId = await RequireSettingsClinicIdAsync();
        if (clinicId is null) return ClinicRequired();
        await LoadAsync(clinicId.Value);
        if (Records.Count == 0) return RedirectToPage();
        var idx = !string.IsNullOrEmpty(UserRecordId) ? Records.FindIndex(r => r.Id == UserRecordId) : 0;
        if (idx < 0) idx = 0;
        idx = Math.Clamp(idx + delta, 0, Records.Count - 1);
        return RedirectToPage(new { UserRecordId = Records[idx].Id, Search });
    }

    private async Task SyncDoctorDisplayAsync(Guid clinicId)
    {
        if (Input.Role != ClinicUserRole.Doctor || Input.DoctorRecordId is not Guid doctorId)
        {
            Input.DoctorDisplayName = string.Empty;
            return;
        }

        var doctor = DoctorOptions.FirstOrDefault(d => d.Id == doctorId)
            ?? await _doctors.GetAsync(clinicId, doctorId);
        Input.DoctorDisplayName = doctor is not null ? FormatDoctorDisplayName(doctor) : string.Empty;
    }

    private static string FormatDoctorDisplayName(Doctor doctor) =>
        string.IsNullOrWhiteSpace(doctor.Specialty)
            ? doctor.Name
            : $"{doctor.Name} — {doctor.Specialty}";

    private async Task<(string? Error, Doctor? Doctor)> ValidateDoctorUserAsync(
        Guid clinicId,
        UserInput input,
        string? excludeUserId = null)
    {
        if (input.Role != ClinicUserRole.Doctor)
        {
            input.DoctorRecordId = null;
            input.DoctorDisplayName = string.Empty;
            return (null, null);
        }

        if (input.DoctorRecordId is null)
            return ("Select a doctor using the Select button before saving.", null);

        var doctor = DoctorOptions.FirstOrDefault(d => d.Id == input.DoctorRecordId)
            ?? await _doctors.GetAsync(clinicId, input.DoctorRecordId.Value);
        if (doctor is null)
            return ("Selected doctor was not found. Please select again.", null);

        var linkedUser = await _users.Users
            .Where(u => u.ClinicId == clinicId && u.DoctorRecordId == input.DoctorRecordId && u.Id != excludeUserId)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync();
        if (linkedUser is not null)
        {
            _logger.LogWarning(
                "Duplicate doctor link blocked: doctor {DoctorId} ({DoctorName}) already linked to user {LinkedUser}",
                doctor.Id, doctor.Name, linkedUser);
            return ($"Doctor \"{doctor.Name}\" is already linked to user \"{linkedUser}\".", null);
        }

        input.DoctorDisplayName = FormatDoctorDisplayName(doctor);
        return (null, doctor);
    }

    public sealed class UserInput
    {
        public int UserNo { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string DoctorDisplayName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Password { get; set; }
        public ClinicUserRole Role { get; set; } = ClinicUserRole.Receptionist;
        public Guid? DoctorRecordId { get; set; }
        public bool IsActive { get; set; } = true;

        public static UserInput FromEntity(ApplicationUser u) => new()
        {
            UserNo = u.UserNo,
            Username = u.UserName ?? "",
            FullName = u.FullName ?? "",
            Email = u.Email,
            Role = u.ClinicRole ?? ClinicUserRole.Receptionist,
            DoctorRecordId = u.DoctorRecordId,
            IsActive = u.IsActive
        };
    }
}
