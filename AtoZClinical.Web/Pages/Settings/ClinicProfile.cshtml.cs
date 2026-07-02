using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
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

    public string PreviewPrimaryColor => ClinicBrandingHelper.NormalizePrimaryColor(Input?.PrimaryColor);

    public async Task<IActionResult> OnGetAsync()
    {
        _logger.LogDebug("ClinicProfile OnGet started for user {User}", User.Identity?.Name);

        if (!await LoadClinicContextAsync())
        {
            _logger.LogWarning("ClinicProfile denied — vendor user {User}", User.Identity?.Name);
            return Page();
        }

        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null)
        {
            _logger.LogWarning("ClinicProfile OnGet — no operational clinic for user {User}", User.Identity?.Name);
            return ClinicRequired();
        }

        return await LoadPageAsync(clinicId.Value);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadClinicContextAsync()) return Page();
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return ClinicRequired();

        if (string.IsNullOrWhiteSpace(Input?.Name))
        {
            ErrorMessage = "Clinic name is required.";
            return Page();
        }

        Input!.LanguageName = Languages.FirstOrDefault(l => l.Code == Input.LanguageCode).Name ?? Input.LanguageName;
        Input.PrimaryColor = ClinicBrandingHelper.NormalizePrimaryColor(Input.PrimaryColor);

        try
        {
            await _profile.SaveAsync(clinicId.Value, Input);
            StatusMessage = "Clinic profile and branding saved.";
            _logger.LogInformation("ClinicProfile saved by user {User} for clinic {ClinicId}", User.Identity?.Name, clinicId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClinicProfile save failed for clinic {ClinicId}", clinicId);
            ErrorMessage = "Could not save clinic profile. Please try again.";
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
            return await LoadPageAsync(clinicId.Value);
        }

        if (logoFile.Length > 512_000)
        {
            ErrorMessage = "Logo must be 500 KB or smaller.";
            return await LoadPageAsync(clinicId.Value);
        }

        try
        {
            await using var ms = new MemoryStream();
            await logoFile.CopyToAsync(ms);
            var base64 = Convert.ToBase64String(ms.ToArray());
            var mime = logoFile.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? logoFile.ContentType
                : "image/png";

            var profile = await _profile.GetAsync(clinicId.Value);
            Input = MapInputFromProfile(profile);
            Input.LogoBase64 = $"data:{mime};base64,{base64}";

            await _profile.SaveAsync(clinicId.Value, Input);
            StatusMessage = "Logo uploaded.";
            _logger.LogInformation("ClinicProfile logo uploaded by user {User} for clinic {ClinicId}", User.Identity?.Name, clinicId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClinicProfile logo upload failed for clinic {ClinicId}", clinicId);
            ErrorMessage = "Could not upload logo. Please try a smaller image.";
            return await LoadPageAsync(clinicId.Value);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostClearLogoAsync()
    {
        if (!await LoadClinicContextAsync()) return Page();
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return ClinicRequired();

        try
        {
            var profile = await _profile.GetAsync(clinicId.Value);
            Input = MapInputFromProfile(profile);
            Input.ClearLogo = true;
            Input.LogoBase64 = null;

            await _profile.SaveAsync(clinicId.Value, Input);
            StatusMessage = "Logo removed.";
            _logger.LogInformation("ClinicProfile logo cleared by user {User} for clinic {ClinicId}", User.Identity?.Name, clinicId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClinicProfile logo clear failed for clinic {ClinicId}", clinicId);
            ErrorMessage = "Could not remove logo. Please try again.";
            return await LoadPageAsync(clinicId.Value);
        }

        return Page();
    }

    private async Task<IActionResult> LoadPageAsync(Guid clinicId)
    {
        try
        {
            var profile = await _profile.GetAsync(clinicId);
            Input = MapInputFromProfile(profile);
            ErrorMessage = null;
            _logger.LogDebug("ClinicProfile page loaded for clinic {ClinicId}", clinicId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClinicProfile load failed for clinic {ClinicId}", clinicId);
            ErrorMessage = "Could not load clinic profile. If this continues after refresh, contact support.";
            Input = CreateSafeFallbackInput();
        }

        return Page();
    }

    private static ClinicProfileInput MapInputFromProfile(ClinicProfileDto profile) => new()
    {
        Name = profile.Name ?? "Clinic",
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

    private static ClinicProfileInput CreateSafeFallbackInput() => new()
    {
        Name = "Clinic",
        TimeZoneId = "UTC",
        LanguageCode = "en",
        LanguageName = "English",
        PrimaryColor = ClinicBrandingHelper.DefaultPrimaryColor,
        FormStyle = "Default"
    };
}
