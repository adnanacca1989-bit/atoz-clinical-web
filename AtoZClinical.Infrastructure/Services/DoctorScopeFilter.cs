namespace AtoZClinical.Infrastructure.Services;

public sealed class DoctorScopeFilter
{
    public static DoctorScopeFilter Unrestricted { get; } = new() { IsRestricted = false };

    public bool IsRestricted { get; init; }
    public Guid? DoctorRecordId { get; init; }
    public string? DoctorName { get; init; }
}

public static class DoctorScopeGuard
{
    public static void EnsureCanAccess(DoctorScopeFilter scope, Guid? doctorRecordId, string? doctorName)
    {
        if (!DoctorScopeQuery.Matches(scope, doctorRecordId, doctorName))
            throw new UnauthorizedAccessException("You do not have access to this record.");
    }
}

public sealed class DoctorScopeContext
{
    public DoctorScopeFilter Filter { get; set; } = DoctorScopeFilter.Unrestricted;
}
