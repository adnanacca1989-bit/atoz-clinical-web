using System.Collections.Concurrent;

namespace AtoZClinical.Infrastructure.Services;

public sealed class ChatPresenceService
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, int>> _connectionsByClinicUser = new();

    public void UserConnected(Guid clinicId, string userId, string connectionId)
    {
        var clinic = _connectionsByClinicUser.GetOrAdd(clinicId, _ => new ConcurrentDictionary<string, int>(StringComparer.Ordinal));
        clinic.AddOrUpdate(userId, 1, (_, count) => count + 1);
    }

    public void UserDisconnected(Guid clinicId, string userId, string connectionId)
    {
        if (!_connectionsByClinicUser.TryGetValue(clinicId, out var clinic))
            return;

        clinic.AddOrUpdate(userId, 0, (_, count) => Math.Max(0, count - 1));
        if (clinic.TryGetValue(userId, out var remaining) && remaining <= 0)
            clinic.TryRemove(userId, out _);

        if (clinic.IsEmpty)
            _connectionsByClinicUser.TryRemove(clinicId, out _);
    }

    public bool IsOnline(Guid clinicId, string userId) =>
        _connectionsByClinicUser.TryGetValue(clinicId, out var clinic) &&
        clinic.TryGetValue(userId, out var count) &&
        count > 0;

    public IReadOnlyList<string> GetOnlineUserIds(Guid clinicId)
    {
        if (!_connectionsByClinicUser.TryGetValue(clinicId, out var clinic))
            return [];

        return clinic.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
    }
}
