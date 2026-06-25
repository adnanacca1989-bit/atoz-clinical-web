using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using AtoZClinical.Infrastructure.Identity;

namespace AtoZClinical.Web.Middleware;

/// <summary>Logs access to routes that expose protected health information (PHI).</summary>
public sealed class PhiAccessAuditMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly (string Prefix, string Label)[] PhiRoutes =
    [
        ("/PatientRegistration", "Patient Registration"),
        ("/Reports/PatientHistory", "Patient History Report"),
        ("/Reports/PatientPrintBundle", "Patient Print Bundle"),
        ("/Reports/PatientStatus", "Patient Status Report"),
        ("/Laboratory/RequestByPatient", "Lab Request By Patient"),
        ("/Radiology/RequestByPatient", "Radiology Request By Patient"),
        ("/Pharmacy/RequestByPatient", "Pharmacy Request By Patient"),
        ("/PatientRegistration/VisitInfo", "Patient Visit Info"),
        ("/PatientRegistration/CloneInfo", "Patient Clone Info"),
        ("/Invoices/PatientCharges", "Patient Charges"),
        ("/Prescriptions", "Prescriptions"),
        ("/Admin/Backup", "Data Backup")
    ];

    public PhiAccessAuditMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        AuditService audit,
        UserManager<ApplicationUser> users)
    {
        await _next(context);

        if (context.User.Identity?.IsAuthenticated != true)
            return;

        if (context.Response.StatusCode >= 400)
            return;

        var path = context.Request.Path.Value ?? string.Empty;
        var match = PhiRoutes.FirstOrDefault(r =>
            path.StartsWith(r.Prefix, StringComparison.OrdinalIgnoreCase));
        if (match == default)
            return;

        try
        {
            var user = await users.GetUserAsync(context.User);
            if (user?.ClinicId is not Guid clinicId)
                return;

            var query = context.Request.QueryString.HasValue
                ? context.Request.QueryString.Value
                : string.Empty;

            await audit.LogAsync(
                clinicId,
                user.UserName,
                match.Label,
                "PHI Access",
                $"GET/POST {path}{query}");
        }
        catch
        {
            // PHI audit must never replace a successful clinical form response.
        }
    }
}
