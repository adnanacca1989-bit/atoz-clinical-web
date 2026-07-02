using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Admin;

public class ResponsibilitiesModel : PageModel
{
    private readonly RolePermissionService _service;
    private readonly ClinicContextService _clinicContext;
    private readonly UserManager<ApplicationUser> _users;

    public ResponsibilitiesModel(
        RolePermissionService service,
        ClinicContextService clinicContext,
        UserManager<ApplicationUser> users)
    {
        _service = service;
        _clinicContext = clinicContext;
        _users = users;
    }

    [BindProperty]
    public string UserRole { get; set; } = "Admin";

    [BindProperty]
    public string? SelectedUserId { get; set; }

    [BindProperty]
    public List<FormPermissionInput> Forms { get; set; } = [];

    public List<ApplicationUser> ClinicUsers { get; private set; } = [];

    public bool RoleHasNoPermissions => Forms.Count > 0 && !Forms.Any(f => f.IsVisible);

    public static readonly string[] Roles = ClinicUserRoleHelper.ResponsibilityRoles;

    public static IEnumerable<(string Key, string Label)> FormDefinitions =>
        ClinicalNavCatalog.DistinctPermissionForms().Select(p => (p.FormKey, p.Label));

    public async Task<IActionResult> OnGetAsync(string? role = null, string? userId = null)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        await LoadUsersAsync(clinicId.Value);

        if (!string.IsNullOrWhiteSpace(userId))
        {
            SelectedUserId = userId;
            var user = ClinicUsers.FirstOrDefault(u => u.Id == userId);
            if (user?.ClinicRole is not null)
                UserRole = ClinicUserRoleHelper.ToResponsibilityRole(user.ClinicRole.Value);
        }
        else if (!string.IsNullOrWhiteSpace(role))
        {
            UserRole = role;
        }

        await LoadFormsAsync(clinicId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        if (Forms.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "No permission rows were received. Please reload the page and try again.");
            await LoadUsersAsync(clinicId.Value);
            await LoadFormsAsync(clinicId.Value);
            return Page();
        }

        var postedByKey = Forms.ToDictionary(f => f.FormKey, f => f.IsVisible, StringComparer.OrdinalIgnoreCase);
        if (!postedByKey.Values.Any(v => v))
        {
            ModelState.AddModelError(string.Empty,
                "This role has no permissions and users will not be able to access the system. Select at least Dashboard.");
            await LoadUsersAsync(clinicId.Value);
            await LoadFormsAsync(clinicId.Value);
            return Page();
        }

        var items = FormDefinitions.Select(fd => new Core.Entities.RolePermission
        {
            FormKey = fd.Key,
            IsVisible = postedByKey.TryGetValue(fd.Key, out var visible) && visible
        });
        await _service.SaveBulkAsync(clinicId.Value, UserRole, items, User.Identity?.Name);

        if (!string.IsNullOrWhiteSpace(SelectedUserId))
        {
            var user = await _users.Users.FirstOrDefaultAsync(u => u.Id == SelectedUserId && u.ClinicId == clinicId);
            if (user is not null)
            {
                user.ClinicRole = ClinicUserRoleHelper.ParseResponsibilityRole(UserRole);
                var result = await _users.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors)
                        ModelState.AddModelError(string.Empty, error.Description);
                    await LoadUsersAsync(clinicId.Value);
                    await LoadFormsAsync(clinicId.Value);
                    return Page();
                }
            }
        }

        return !string.IsNullOrWhiteSpace(SelectedUserId)
            ? RedirectToPage(new { userId = SelectedUserId })
            : RedirectToPage(new { role = UserRole });
    }

    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task LoadUsersAsync(Guid clinicId)
    {
        ClinicUsers = await _users.Users
            .Where(u => u.ClinicId == clinicId && u.IsActive)
            .OrderBy(u => u.UserName)
            .ToListAsync();
    }

    private async Task LoadFormsAsync(Guid clinicId)
    {
        var existing = await _service.ListForRoleAsync(clinicId, UserRole);
        if (existing.Count == 0 && UserRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            existing = await _service.ListForRoleAsync(clinicId, ClinicalRoles.ClinicAdmin);

        string? lastSection = null;
        string? lastGroup = null;
        Forms = ClinicalNavCatalog.DistinctPermissionForms().Select(fd =>
        {
            var match = existing.FirstOrDefault(e => e.FormKey == fd.FormKey);
            var defaultVisible = UserRole.Equals("Admin", StringComparison.OrdinalIgnoreCase);
            var showSection = !string.Equals(lastSection, fd.Section, StringComparison.Ordinal);
            var showGroup = showSection
                || !string.Equals(lastGroup, fd.Group, StringComparison.Ordinal);
            lastSection = fd.Section;
            lastGroup = fd.Group;
            return new FormPermissionInput
            {
                FormKey = fd.FormKey,
                FormLabel = fd.Label,
                SectionLabel = fd.Section,
                GroupLabel = fd.Group,
                ShowSectionHeader = showSection,
                ShowGroupHeader = showGroup && !string.IsNullOrWhiteSpace(fd.Group),
                IsVisible = match?.IsVisible ?? defaultVisible
            };
        }).ToList();
    }

    public sealed class FormPermissionInput
    {
        public string FormKey { get; set; } = string.Empty;
        public string FormLabel { get; set; } = string.Empty;
        public string SectionLabel { get; set; } = string.Empty;
        public string? GroupLabel { get; set; }
        public bool ShowSectionHeader { get; set; }
        public bool ShowGroupHeader { get; set; }
        public bool IsVisible { get; set; } = true;
    }
}
