using AtoZClinical.Core.Enums;

namespace AtoZClinical.Infrastructure.Services;

public static class ClinicUserRoleHelper
{
    public static readonly (ClinicUserRole Value, string Label)[] DefineUserRoles =
    [
        (ClinicUserRole.ClinicAdmin, "Admin"),
        (ClinicUserRole.Doctor, "Doctor"),
        (ClinicUserRole.Receptionist, "Reception"),
        (ClinicUserRole.LabTechnician, "Lab"),
        (ClinicUserRole.Radiology, "Radiology"),
        (ClinicUserRole.Pharmacist, "Cashier")
    ];

    public static readonly string[] ResponsibilityRoles =
        ["Radiology", "Admin", "Doctor", "Reception", "Lab", "Cashier"];

    public static string GetLabel(ClinicUserRole role) =>
        DefineUserRoles.FirstOrDefault(r => r.Value == role).Label ?? role.ToString();

    public static ClinicUserRole ParseResponsibilityRole(string roleName) => roleName switch
    {
        "Doctor" => ClinicUserRole.Doctor,
        "Reception" => ClinicUserRole.Receptionist,
        "Lab" => ClinicUserRole.LabTechnician,
        "Radiology" => ClinicUserRole.Radiology,
        "Cashier" => ClinicUserRole.Pharmacist,
        _ => ClinicUserRole.ClinicAdmin
    };

    public static string ToResponsibilityRole(ClinicUserRole role) => role switch
    {
        ClinicUserRole.Doctor => "Doctor",
        ClinicUserRole.Receptionist => "Reception",
        ClinicUserRole.Nurse => "Reception",
        ClinicUserRole.LabTechnician => "Lab",
        ClinicUserRole.Radiology => "Radiology",
        ClinicUserRole.Pharmacist => "Cashier",
        _ => "Admin"
    };
}
