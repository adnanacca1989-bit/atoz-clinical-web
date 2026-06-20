namespace AtoZClinical.Core.Entities;

public class RolePermission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string FormKey { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;

    public Clinic Clinic { get; set; } = null!;
}
