namespace AtoZClinical.Infrastructure.Services;

public interface IClinicalNotificationPublisher
{
    Task PublishDepartmentAsync(
        Guid clinicId,
        string targetRole,
        string title,
        string detail,
        string link,
        CancellationToken ct = default);
}
