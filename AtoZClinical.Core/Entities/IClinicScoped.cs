namespace AtoZClinical.Core.Entities;

/// <summary>Marks entities that belong to a single clinic tenant.</summary>
public interface IClinicScoped
{
    Guid ClinicId { get; set; }
}
