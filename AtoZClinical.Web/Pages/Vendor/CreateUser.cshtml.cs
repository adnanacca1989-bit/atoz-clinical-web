using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AtoZClinical.Web.Pages.Vendor;

public class CreateUserModel : PageModel
{
    private readonly VendorClinicService _vendor;

    public CreateUserModel(VendorClinicService vendor) => _vendor = vendor;

    [BindProperty]
    public UserInput Input { get; set; } = new();

    public string ClinicName { get; private set; } = string.Empty;
    public List<SelectListItem> RoleOptions { get; } = Enum.GetValues<ClinicUserRole>()
        .Select(r => new SelectListItem(r.ToString(), r.ToString()))
        .ToList();

    public async Task<IActionResult> OnGetAsync(Guid clinicId)
    {
        var clinic = await _vendor.GetClinicAsync(clinicId);
        if (clinic is null) return NotFound();
        ClinicName = clinic.Name;
        Input.ClinicId = clinicId;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var clinic = await _vendor.GetClinicAsync(Input.ClinicId);
        if (clinic is null) return NotFound();
        ClinicName = clinic.Name;

        if (!ModelState.IsValid) return Page();

        try
        {
            var (user, password) = await _vendor.CreateClinicUserAsync(new CreateClinicUserRequest
            {
                ClinicId = Input.ClinicId,
                Username = Input.Username,
                FullName = Input.FullName,
                Email = Input.Email,
                Password = Input.Password,
                Role = Input.Role
            });

            TempData["NewUsername"] = user.UserName;
            TempData["NewPassword"] = password;
            return RedirectToPage("Details", new { id = Input.ClinicId });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    public sealed class UserInput
    {
        public Guid ClinicId { get; set; }

        [Required, Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        public string Username { get; set; } = string.Empty;

        public string? Email { get; set; }

        [Required]
        public ClinicUserRole Role { get; set; } = ClinicUserRole.Receptionist;

        public string? Password { get; set; }
    }
}
