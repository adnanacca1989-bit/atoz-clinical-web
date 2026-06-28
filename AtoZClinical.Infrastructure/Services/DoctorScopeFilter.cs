using Microsoft.Extensions.Logging;

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
    public static void EnsureCanAccess(
        DoctorScopeFilter scope,
        Guid? doctorRecordId,
        string? doctorName,
        ILogger? logger = null,
        string? context = null)
    {
        if (DoctorScopeQuery.Matches(scope, doctorRecordId, doctorName))
            return;

        logger?.LogWarning(
            "Doctor scope access denied{Context}: filterDoctorId={FilterDoctorId} filterName={FilterName} recordDoctorId={RecordDoctorId} recordName={RecordName}",
            string.IsNullOrWhiteSpace(context) ? "" : $" ({context})",
            scope.DoctorRecordId,
            scope.DoctorName,
            doctorRecordId,
            doctorName);

        throw new UnauthorizedAccessException("You do not have access to this record.");
    }
}

public sealed class DoctorScopeContext
{
    public DoctorScopeFilter Filter { get; set; } = DoctorScopeFilter.Unrestricted;
}
