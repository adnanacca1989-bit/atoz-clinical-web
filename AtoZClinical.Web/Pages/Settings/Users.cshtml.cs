using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Settings;

public class UsersModel : SettingsFormPageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly VendorClinicService _vendor;

    public UsersModel(ClinicContextService clinicContext, UserManager<ApplicationUser> users, VendorClinicService vendor)
        : base(clinicContext)
    {
        _users = users;
        _vendor = vendor;
    }

    [BindProperty(SupportsGet = true)]
    public string? UserRecordId { get; set; }

    [BindProperty] public UserInput Input { get; set; } = new();
    public List<ApplicationUser> Records { get; private set; } = [];
    public bool IsNewUser => string.IsNullOrEmpty(UserRecordId);

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireSettingsClinicIdAsync();
        if (clinicId is null) return ClinicRequired();
        await LoadAsync(clinicId.Value);
        if (!string.IsNullOrEmpty(UserRecordId)) await LoadRecord(clinicId.Value, UserRecordId);
        else if (Records.Count > 0) await LoadRecord(clinicId.Value, Records[0].Id);
        else await PrepareNew();
        SetFormViewData("Define User", null, null, null);
        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync();
    public Task<IActionResult> OnPostNewAsync() => Task.FromResult<IActionResult>(RedirectToPage());
    public Task<IActionResult> OnPostClearAsync() => Task.FromResult<IActionResult>(RedirectToPage());
    public Task<IActionResult> OnPostDeleteAsync() => DeleteCoreAsync();
    public Task<IActionResult> OnPostBackAsync() => NavigateCoreAsync(-1);
    public Task<IActionResult> OnPostNextAsync() => NavigateCoreAsync(1);

    private async Task LoadAsync(Guid clinicId)
    {
        Records = await _users.Users.Where(u => u.ClinicId == clinicId).OrderBy(u => u.UserName).ToListAsync();
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
    }

    private Task PrepareNew()
    {
        UserRecordId = null;
        Input = new UserInput { IsActive = true, Role = ClinicUserRole.Receptionist };
        return Task.CompletedTask;
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireSettingsClinicIdAsync();
        if (clinicId is null) return ClinicRequired();

        if (IsNewUser)
        {
            if (string.IsNullOrWhiteSpace(Input.Username) || string.IsNullOrWhiteSpace(Input.FullName))
            {
                ModelState.AddModelError(string.Empty, "Username and Full Name are required.");
                await LoadAsync(clinicId.Value);
                return Page();
            }
            try
            {
                var (_, _) = await _vendor.CreateClinicUserAsync(new CreateClinicUserRequest
                {
                    ClinicId = clinicId.Value,
                    Username = Input.Username,
                    FullName = Input.FullName,
                    Email = Input.Email,
                    Password = Input.Password,
                    Role = Input.Role
                });
                var created = await _users.FindByNameAsync(Input.Username);
                if (SaveMode == "New")
                    return RedirectToPage(new { Search });
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
        user.FullName = Input.FullName.Trim();
        user.Email = Input.Email?.Trim();
        user.ClinicRole = Input.Role;
        user.IsActive = Input.IsActive;
        await _users.UpdateAsync(user);
        if (!string.IsNullOrWhiteSpace(Input.Password))
        {
            await _users.RemovePasswordAsync(user);
            await _users.AddPasswordAsync(user, Input.Password);
        }
        if (SaveMode == "New")
            return RedirectToPage(new { Search });
        return RedirectToPage(new { UserRecordId = user.Id, Search });
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

    public sealed class UserInput
    {
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Password { get; set; }
        public ClinicUserRole Role { get; set; } = ClinicUserRole.Receptionist;
        public bool IsActive { get; set; } = true;

        public static UserInput FromEntity(ApplicationUser u) => new()
        {
            Username = u.UserName ?? "",
            FullName = u.FullName ?? "",
            Email = u.Email,
            Role = u.ClinicRole ?? ClinicUserRole.Receptionist,
            IsActive = u.IsActive
        };
    }
}
