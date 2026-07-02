using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Settings;

public class ClinicProfileModel : SettingsPageModel
{
    private readonly ClinicProfileService _profile;
    private readonly ILogger<ClinicProfileModel> _logger;

    public ClinicProfileModel(
        ClinicContextService clinicContext,
        ClinicSettingsService settingsService,
        ClinicProfileService profile,
        ILogger<ClinicProfileModel> logger) : base(clinicContext, settingsService)
    {
        _profile = profile;
        _logger = logger;
    }

    [BindProperty]
    public ClinicProfileInput Input { get; set; } = new();

    public string? StatusMessage { get; private set; }
    public string? ErrorMessage { get; private set; }

    public static readonly string[] TimeZones =
    [
        "UTC", "Asia/Baghdad", "Asia/Dubai", "Asia/Riyadh", "Europe/London",
        "Europe/Berlin", "America/New_York", "America/Chicago", "America/Los_Angeles"
    ];

    public static readonly (string Code, string Name)[] Languages =
    [
        ("en", "English"),
        ("ar", "Arabic"),
        ("ku", "Kurdish")
    ];

    public static readonly string[] FormStyles = ["Default", "Compact", "Large"];

    public static IEnumerable<SelectListItem> TimeZoneSelectItems =>
        TimeZones.Select(t => new SelectListItem(t, t));

    public static IEnumerable<SelectListItem> LanguageSelectItems =>
        Languages.Select(l => new SelectListItem(l.Name, l.Code));

    public static IEnumerable<SelectListItem> FormStyleSelectItems =>
        FormStyles.Select(s => new SelectListItem(s, s));

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadClinicContextAsync()) return Page();
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return ClinicRequired();

        try
        {
            var profile = await _profile.GetAsync(clinicId.Value);
            Input = new ClinicProfileInput
            {
                Name = profile.Name,
                ContactPerson = profile.ContactPerson,
                Email = profile.Email,
                Phone = profile.Phone,
                Address = profile.Address,
                City = profile.City,
                Country = profile.Country,
                TimeZoneId = profile.TimeZoneId,
                LanguageCode = profile.LanguageCode,
                LanguageName = profile.LanguageName,
                LogoBase64 = profile.LogoBase64,
                Tagline = profile.Tagline,
                Website = profile.Website,
                PrimaryColor = profile.PrimaryColor,
                FormStyle = profile.FormStyle
            };
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load clinic profile for clinic {ClinicId}", clinicId);
            ErrorMessage = "Could not load clinic profile. If this continues after refresh, contact support.";
            Input.Name = "Clinic";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadClinicContextAsync()) return Page();
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return ClinicRequired();

        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            ErrorMessage = "Clinic name is required.";
            return Page();
        }

        Input.LanguageName = Languages.FirstOrDefault(l => l.Code == Input.LanguageCode).Name ?? Input.LanguageName;

        try
        {
            await _profile.SaveAsync(clinicId.Value, Input);
            StatusMessage = "Clinic profile and branding saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostUploadLogoAsync(IFormFile? logoFile)
    {
        if (!await LoadClinicContextAsync()) return Page();
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return ClinicRequired();

        if (logoFile is null || logoFile.Length == 0)
        {
            ErrorMessage = "Please choose an image file.";
            return await OnGetAsync();
        }

        if (logoFile.Length > 512_000)
        {
            ErrorMessage = "Logo must be 500 KB or smaller.";
            return await OnGetAsync();
        }

        await using var ms = new MemoryStream();
        await logoFile.CopyToAsync(ms);
        var base64 = Convert.ToBase64String(ms.ToArray());
        var mime = logoFile.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? logoFile.ContentType
            : "image/png";
        Input.LogoBase64 = $"data:{mime};base64,{base64}";

        var profile = await _profile.GetAsync(clinicId.Value);
        Input.Name = profile.Name;
        Input.ContactPerson = profile.ContactPerson;
        Input.Email = profile.Email;
        Input.Phone = profile.Phone;
        Input.Address = profile.Address;
        Input.City = profile.City;
        Input.Country = profile.Country;
        Input.TimeZoneId = profile.TimeZoneId;
        Input.LanguageCode = profile.LanguageCode;
        Input.LanguageName = profile.LanguageName;
        Input.Tagline = profile.Tagline;
        Input.Website = profile.Website;
        Input.PrimaryColor = profile.PrimaryColor;
        Input.FormStyle = profile.FormStyle;

        await _profile.SaveAsync(clinicId.Value, Input);
        StatusMessage = "Logo uploaded.";
        return Page();
    }

    public async Task<IActionResult> OnPostClearLogoAsync()
    {
        if (!await LoadClinicContextAsync()) return Page();
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return ClinicRequired();

        var profile = await _profile.GetAsync(clinicId.Value);
        Input = new ClinicProfileInput
        {
            Name = profile.Name,
            ContactPerson = profile.ContactPerson,
            Email = profile.Email,
            Phone = profile.Phone,
            Address = profile.Address,
            City = profile.City,
            Country = profile.Country,
            TimeZoneId = profile.TimeZoneId,
            LanguageCode = profile.LanguageCode,
            LanguageName = profile.LanguageName,
            Tagline = profile.Tagline,
            Website = profile.Website,
            PrimaryColor = profile.PrimaryColor,
            FormStyle = profile.FormStyle,
            ClearLogo = true
        };

        await _profile.SaveAsync(clinicId.Value, Input);
        Input.LogoBase64 = null;
        StatusMessage = "Logo removed.";
        return Page();
    }
}
