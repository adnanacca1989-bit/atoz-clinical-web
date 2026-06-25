namespace AtoZClinical.Infrastructure.Data;

/// <summary>Provides the active clinic tenant for EF global query filters.</summary>
public interface ICurrentClinicProvider
{
  Guid? ClinicId { get; }
  bool BypassTenantFilter { get; }
}

public sealed class BypassClinicProvider : ICurrentClinicProvider
{
  public static readonly BypassClinicProvider Instance = new();
  public Guid? ClinicId => null;
  public bool BypassTenantFilter => true;
}
