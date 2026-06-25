using AtoZClinical.Infrastructure.Data;

namespace AtoZClinical.Tests.Helpers;

public sealed class TestClinicProvider : ICurrentClinicProvider
{
    public Guid? ClinicId { get; set; }
    public bool BypassTenantFilter { get; set; }

    public static TestClinicProvider ForClinic(Guid clinicId) => new() { ClinicId = clinicId };
    public static TestClinicProvider Bypass() => new() { BypassTenantFilter = true };
}
