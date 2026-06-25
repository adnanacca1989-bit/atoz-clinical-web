using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AtoZClinical.Web.Services;

public sealed class PatientPortalSession
{
    public const string CookieName = "atz_patient_portal";

    private readonly IDataProtector _protector;

    public PatientPortalSession(IDataProtectionProvider dataProtection)
    {
        _protector = dataProtection.CreateProtector("AtoZClinical.PatientPortal.v1");
    }

    public void SignIn(HttpContext context, Guid clinicId, Guid patientId)
    {
        var payload = JsonSerializer.Serialize(new SessionData(clinicId, patientId, DateTime.UtcNow));
        var protectedValue = _protector.Protect(payload);
        context.Response.Cookies.Append(CookieName, protectedValue, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromHours(2),
            IsEssential = true
        });
    }

    public void SignOut(HttpContext context) =>
        context.Response.Cookies.Delete(CookieName);

    public SessionData? Get(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value))
            return null;

        try
        {
            var json = _protector.Unprotect(value);
            var data = JsonSerializer.Deserialize<SessionData>(json);
            if (data is null || data.IssuedAt < DateTime.UtcNow.AddHours(-8))
                return null;
            return data;
        }
        catch
        {
            return null;
        }
    }

    public sealed record SessionData(Guid ClinicId, Guid PatientId, DateTime IssuedAt);
}
