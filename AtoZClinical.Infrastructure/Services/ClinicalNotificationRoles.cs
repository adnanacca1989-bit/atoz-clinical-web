using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Identity;

namespace AtoZClinical.Infrastructure.Services;

public static class ClinicalNotificationRoles
{
    public const string Laboratory = "Laboratory";
    public const string Lab = "Lab";
    public const string Radiology = "Radiology";
    public const string Pharmacy = "Pharmacy";
    public const string Cashier = "Cashier";

    public static string ForLab() => Laboratory;
    public static string ForRadiology() => Radiology;
    public static string ForPharmacy() => Pharmacy;

    public static bool UserReceivesNotification(ApplicationUser user, string targetRole)
    {
        if (user.IsVendorAdmin || user.ClinicRole is ClinicUserRole.ClinicAdmin)
            return true;

        return user.ClinicRole switch
        {
            ClinicUserRole.LabTechnician when MatchesLab(targetRole) => true,
            ClinicUserRole.Radiology when MatchesRadiology(targetRole) => true,
            ClinicUserRole.Pharmacist when MatchesPharmacy(targetRole) => true,
            _ => false
        };
    }

    public static string? SignalRGroupForUser(ApplicationUser user)
    {
        if (user.ClinicId is null) return null;
        return user.ClinicRole switch
        {
            ClinicUserRole.LabTechnician => RoleGroup(user.ClinicId.Value, Laboratory),
            ClinicUserRole.Radiology => RoleGroup(user.ClinicId.Value, Radiology),
            ClinicUserRole.Pharmacist => RoleGroup(user.ClinicId.Value, Pharmacy),
            ClinicUserRole.ClinicAdmin => ClinicGroup(user.ClinicId.Value),
            _ => null
        };
    }

    public static string ClinicGroup(Guid clinicId) => $"notify-clinic-{clinicId:N}";
    public static string RoleGroup(Guid clinicId, string role) => $"notify-{clinicId:N}-{role}";

    private static bool MatchesLab(string role) =>
        role.Equals(Laboratory, StringComparison.OrdinalIgnoreCase) ||
        role.Equals(Lab, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesRadiology(string role) =>
        role.Equals(Radiology, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesPharmacy(string role) =>
        role.Equals(Pharmacy, StringComparison.OrdinalIgnoreCase) ||
        role.Equals(Cashier, StringComparison.OrdinalIgnoreCase);
}
