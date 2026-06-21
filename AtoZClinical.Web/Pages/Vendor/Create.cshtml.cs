using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Vendor;

public class CreateModel : PageModel
{
    private readonly VendorClinicService _vendor;

    public CreateModel(VendorClinicService vendor) => _vendor = vendor;

    [BindProperty]
    public CreateClientInput Input { get; set; } = new();

    public void OnGet()
    {
        Input.DatabasePort = 5432;
        Input.LicenseExpires = DateTime.Today.AddYears(1);
        Input.PlanName = "Standard";
        Input.MaxUsers = 25;
        Input.ActivateImmediately = true;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        try
        {
            var (clinic, admin, password) = await _vendor.CreateClinicAsync(new CreateClinicRequest
            {
                Name = Input.Name,
                ClinicCode = Input.ClinicCode,
                ContactPerson = Input.ContactPerson,
                Email = Input.Email,
                Phone = Input.Phone,
                Address = Input.Address,
                City = Input.City,
                Country = Input.Country,
                HostingUrl = Input.HostingUrl,
                DatabaseHost = Input.DatabaseHost,
                DatabasePort = Input.DatabasePort,
                DatabaseName = Input.DatabaseName,
                LicenseExpires = Input.LicenseExpires,
                PlanName = Input.PlanName,
                MaxUsers = Input.MaxUsers,
                ActivateImmediately = Input.ActivateImmediately,
                Notes = Input.Notes,
                AdminUsername = Input.AdminUsername,
                AdminPassword = Input.AdminPassword
            });

            TempData["NewPassword"] = password;
            TempData["NewUsername"] = admin.UserName;
            return RedirectToPage("Details", new { id = clinic.Id, created = true });
        }
        catch (DbUpdateException ex)
        {
            ModelState.AddModelError(string.Empty, ex.InnerException?.Message ?? ex.Message);
            return Page();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    public sealed class CreateClientInput
    {
        [Required, Display(Name = "Clinic Name")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Clinic Code")]
        public string? ClinicCode { get; set; }

        [Display(Name = "Contact Person")]
        public string? ContactPerson { get; set; }

        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }

        [Display(Name = "Web Address (URL)")]
        public string? HostingUrl { get; set; }

        [Display(Name = "Database Host")]
        public string? DatabaseHost { get; set; }

        [Display(Name = "Database Port")]
        public int DatabasePort { get; set; } = 5432;

        [Display(Name = "Database Name")]
        public string? DatabaseName { get; set; }

        [Display(Name = "License Expires")]
        public DateTime? LicenseExpires { get; set; }

        [Display(Name = "Subscription Plan")]
        public string PlanName { get; set; } = "Standard";

        [Display(Name = "Max Users")]
        public int MaxUsers { get; set; } = 25;

        [Display(Name = "Activate Immediately")]
        public bool ActivateImmediately { get; set; }

        public string? Notes { get; set; }

        [Required, Display(Name = "Admin Username")]
        public string AdminUsername { get; set; } = string.Empty;

        [Display(Name = "Admin Password")]
        public string? AdminPassword { get; set; }
    }
}
