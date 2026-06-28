namespace AtoZClinical.Infrastructure.Services;

public sealed class DoctorScopeFilter
{
    public static DoctorScopeFilter Unrestricted { get; } = new() { IsRestricted = false };

    public bool IsRestricted { get; init; }
    public Guid? DoctorRecordId { get; init; }
    public string? DoctorName { get; init; }
}

public sealed class DoctorScopeContext
{
    public DoctorScopeFilter Filter { get; set; } = DoctorScopeFilter.Unrestricted;
}
