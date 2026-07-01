using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class DoctorSurgeryService
{
    public const int TotalWardRooms = 50;

    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly DoctorScopeContext _doctorScope;

    public DoctorSurgeryService(ClinicalDbContext db, AuditService audit, DoctorScopeContext doctorScope)
    {
        _db = db;
        _audit = audit;
        _doctorScope = doctorScope;
    }

    public Task<List<DoctorSurgery>> ListAsync(Guid clinicId) =>
        _db.DoctorSurgeries.ForClinic(clinicId).Apply(_doctorScope.Filter)
            .OrderByDescending(s => s.SurgeryNo).ToListAsync();

    public async Task<DoctorSurgery?> GetAsync(Guid clinicId, Guid id)
    {
        var item = await _db.DoctorSurgeries.ForClinic(clinicId).FirstOrDefaultAsync(s => s.Id == id);
        if (item is null || !DoctorScopeQuery.Matches(_doctorScope.Filter, item.DoctorRecordId, item.DoctorName))
            return null;
        return item;
    }

    public async Task<DoctorSurgery?> GetLatestForPatientAsync(Guid clinicId, Guid patientId) =>
        await _db.DoctorSurgeries.ForClinic(clinicId)
            .Where(s => s.PatientRecordId == patientId)
            .OrderByDescending(s => s.SurgeryDate)
            .ThenByDescending(s => s.SurgeryNo)
            .FirstOrDefaultAsync();

    public async Task<DoctorSurgery> SaveAsync(Guid clinicId, DoctorSurgery item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        if (!isNew)
        {
            var owned = await GetAsync(clinicId, item.Id);
            if (owned is null)
                throw new UnauthorizedAccessException("You do not have access to this surgery record.");
            item.ClinicId = clinicId;
            item.UpdatedAt = DateTime.UtcNow;
            _db.DoctorSurgeries.Update(item);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(clinicId, userName, "Doctor Surgery", "Update",
                $"Surgery #{item.SurgeryNo} — {item.PatientName}");
            return item;
        }

        var template = item;
        item = await ClinicSaveHelper.InsertWithSequenceRetryAsync(
            _db,
            async attempt =>
            {
                var row = CloneShell(template);
                row.Id = Guid.NewGuid();
                row.ClinicId = clinicId;
                row.SurgeryNo = await NextSurgeryNoAsync(clinicId, attempt);
                row.CreatedAt = DateTime.UtcNow;
                row.UpdatedAt = DateTime.UtcNow;
                return row;
            },
            row => _db.DoctorSurgeries.Add(row),
            ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_DoctorSurgeries_ClinicId_SurgeryNo"),
            failureMessage: "Could not save doctor surgery record");

        await _audit.LogAsync(clinicId, userName, "Doctor Surgery", "Create",
            $"Surgery #{item.SurgeryNo} — {item.PatientName}");
        return item;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        _db.DoctorSurgeries.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Doctor Surgery", "Delete",
            $"Surgery #{item.SurgeryNo} — {item.PatientName}");
    }

    private async Task<int> NextSurgeryNoAsync(Guid clinicId, int skip = 0)
    {
        var max = await _db.DoctorSurgeries.ForClinic(clinicId).MaxAsync(s => (int?)s.SurgeryNo) ?? 0;
        return max + 1 + skip;
    }

    private static DoctorSurgery CloneShell(DoctorSurgery source) => new()
    {
        RecordDate = source.RecordDate,
        SurgeryDate = source.SurgeryDate,
        SurgeryTime = source.SurgeryTime,
        PatientRecordId = source.PatientRecordId,
        PatientName = source.PatientName,
        PatientBarcode = source.PatientBarcode,
        Age = source.Age,
        City = source.City,
        NationalId = source.NationalId,
        Phone = source.Phone,
        MotherName = source.MotherName,
        DoctorRecordId = source.DoctorRecordId,
        DoctorName = source.DoctorName,
        Specialty = source.Specialty,
        TypeOfSurgery = source.TypeOfSurgery,
        Classify = source.Classify,
        SurgeryName = source.SurgeryName,
        InitialAmount = source.InitialAmount
    };
}

public sealed class RoomBookingService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly WardRoomService _wardRooms;

    public RoomBookingService(ClinicalDbContext db, AuditService audit, WardRoomService wardRooms)
    {
        _db = db;
        _audit = audit;
        _wardRooms = wardRooms;
    }

    public Task<List<RoomBooking>> ListAsync(Guid clinicId) =>
        _db.RoomBookings.ForClinic(clinicId).OrderByDescending(b => b.BookingNo).ToListAsync();

    public Task<RoomBooking?> GetAsync(Guid clinicId, Guid id) =>
        _db.RoomBookings.ForClinic(clinicId).FirstOrDefaultAsync(b => b.Id == id);

    public async Task<RoomBooking> SaveAsync(Guid clinicId, RoomBooking item, string? userName = null)
    {
        item.Days = ComputeDays(item.EnterDate, item.ExitDate);

        var isNew = item.Id == Guid.Empty;
        if (!isNew)
        {
            item.ClinicId = clinicId;
            item.UpdatedAt = DateTime.UtcNow;
            _db.RoomBookings.Update(item);
            await _db.SaveChangesAsync();
            await _wardRooms.SyncFromBookingAsync(clinicId, item);
            await _audit.LogAsync(clinicId, userName, "Book Room", "Update",
                $"Booking #{item.BookingNo} — Room {item.RoomNumber} — {item.PatientName}");
            return item;
        }

        var template = item;
        item = await ClinicSaveHelper.InsertWithSequenceRetryAsync(
            _db,
            async attempt =>
            {
                var row = CloneShell(template);
                row.Id = Guid.NewGuid();
                row.ClinicId = clinicId;
                row.BookingNo = await NextBookingNoAsync(clinicId, attempt);
                row.Days = ComputeDays(row.EnterDate, row.ExitDate);
                row.CreatedAt = DateTime.UtcNow;
                row.UpdatedAt = DateTime.UtcNow;
                return row;
            },
            row => _db.RoomBookings.Add(row),
            ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_RoomBookings_ClinicId_BookingNo"),
            failureMessage: "Could not save room booking");

        await _wardRooms.SyncFromBookingAsync(clinicId, item);
        await _audit.LogAsync(clinicId, userName, "Book Room", "Create",
            $"Booking #{item.BookingNo} — Room {item.RoomNumber} — {item.PatientName}");
        return item;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        _db.RoomBookings.Remove(item);
        await _db.SaveChangesAsync();
        if (item.RoomNumber is int roomNo)
            await _wardRooms.RefreshRoomStatusAsync(clinicId, roomNo);
        await _audit.LogAsync(clinicId, userName, "Book Room", "Delete",
            $"Booking #{item.BookingNo} — Room {item.RoomNumber}");
    }

    public static int? ComputeDays(DateTime? enterDate, DateTime? exitDate)
    {
        if (enterDate is null || exitDate is null) return null;
        var days = (exitDate.Value.Date - enterDate.Value.Date).Days;
        return Math.Max(1, days + 1);
    }

    private async Task<int> NextBookingNoAsync(Guid clinicId, int skip = 0)
    {
        var max = await _db.RoomBookings.ForClinic(clinicId).MaxAsync(b => (int?)b.BookingNo) ?? 0;
        return max + 1 + skip;
    }

    private static RoomBooking CloneShell(RoomBooking source) => new()
    {
        DateBook = source.DateBook,
        PatientRecordId = source.PatientRecordId,
        DoctorSurgeryId = source.DoctorSurgeryId,
        PatientName = source.PatientName,
        PatientBarcode = source.PatientBarcode,
        Age = source.Age,
        City = source.City,
        NationalId = source.NationalId,
        Phone = source.Phone,
        MotherName = source.MotherName,
        DoctorName = source.DoctorName,
        Specialty = source.Specialty,
        TypeOfSurgery = source.TypeOfSurgery,
        Classify = source.Classify,
        SurgeryName = source.SurgeryName,
        RoomNumber = source.RoomNumber,
        EnterDate = source.EnterDate,
        ExitDate = source.ExitDate,
        EnterTime = source.EnterTime,
        ExitTime = source.ExitTime,
        Note = source.Note
    };
}

public sealed class WardRoomBoard
{
    public int TotalRooms { get; set; }
    public int RemainingCount { get; set; }
    public int EmptyCount { get; set; }
    public int BookedCount { get; set; }
    public List<WardRoomCell> Rooms { get; set; } = [];
}

public sealed class WardRoomCell
{
    public int RoomNo { get; set; }
    public string Status { get; set; } = WardRoomStatuses.Remaining;
    public string? PatientName { get; set; }
}

public sealed class WardRoomService
{
    private readonly ClinicalDbContext _db;

    public WardRoomService(ClinicalDbContext db) => _db = db;

    public async Task<WardRoomBoard> GetBoardAsync(Guid clinicId)
    {
        await EnsureRoomsAsync(clinicId);
        var rooms = await _db.WardRooms.ForClinic(clinicId).OrderBy(r => r.RoomNo).ToListAsync();
        var activeBookings = await _db.RoomBookings.ForClinic(clinicId)
            .Where(b => b.RoomNumber != null && b.EnterDate != null && b.ExitDate == null)
            .ToListAsync();
        var bookingByRoom = activeBookings
            .GroupBy(b => b.RoomNumber!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.EnterDate).First());

        var cells = rooms.Select(r =>
        {
            bookingByRoom.TryGetValue(r.RoomNo, out var booking);
            return new WardRoomCell
            {
                RoomNo = r.RoomNo,
                Status = r.Status,
                PatientName = booking?.PatientName
            };
        }).ToList();

        return new WardRoomBoard
        {
            TotalRooms = DoctorSurgeryService.TotalWardRooms,
            RemainingCount = cells.Count(c => c.Status == WardRoomStatuses.Remaining),
            EmptyCount = cells.Count(c => c.Status == WardRoomStatuses.Empty),
            BookedCount = cells.Count(c => c.Status == WardRoomStatuses.Booked),
            Rooms = cells
        };
    }

    public async Task EnsureRoomsAsync(Guid clinicId)
    {
        var existing = await _db.WardRooms.ForClinic(clinicId).Select(r => r.RoomNo).ToListAsync();
        var missing = Enumerable.Range(1, DoctorSurgeryService.TotalWardRooms)
            .Where(n => !existing.Contains(n))
            .ToList();
        if (missing.Count == 0) return;

        foreach (var roomNo in missing)
        {
            _db.WardRooms.Add(new WardRoom
            {
                ClinicId = clinicId,
                RoomNo = roomNo,
                Status = WardRoomStatuses.Remaining
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task SyncFromBookingAsync(Guid clinicId, RoomBooking booking)
    {
        if (booking.RoomNumber is not int roomNo) return;
        await EnsureRoomsAsync(clinicId);
        var room = await _db.WardRooms.ForClinic(clinicId).FirstOrDefaultAsync(r => r.RoomNo == roomNo);
        if (room is null) return;

        if (booking.EnterDate.HasValue && !booking.ExitDate.HasValue)
            room.Status = WardRoomStatuses.Booked;
        else if (booking.ExitDate.HasValue)
            room.Status = WardRoomStatuses.Empty;
        else
            await RefreshRoomStatusAsync(clinicId, roomNo);

        room.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task RefreshRoomStatusAsync(Guid clinicId, int roomNo)
    {
        await EnsureRoomsAsync(clinicId);
        var room = await _db.WardRooms.ForClinic(clinicId).FirstOrDefaultAsync(r => r.RoomNo == roomNo);
        if (room is null) return;

        var active = await _db.RoomBookings.ForClinic(clinicId)
            .AnyAsync(b => b.RoomNumber == roomNo && b.EnterDate != null && b.ExitDate == null);

        room.Status = active
            ? WardRoomStatuses.Booked
            : WardRoomStatuses.Remaining;
        room.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task MarkRoomEmptyAsync(Guid clinicId, int roomNo)
    {
        await EnsureRoomsAsync(clinicId);
        var room = await _db.WardRooms.ForClinic(clinicId).FirstOrDefaultAsync(r => r.RoomNo == roomNo);
        if (room is null) return;
        room.Status = WardRoomStatuses.Empty;
        room.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task MarkRoomAvailableAsync(Guid clinicId, int roomNo)
    {
        await EnsureRoomsAsync(clinicId);
        var room = await _db.WardRooms.ForClinic(clinicId).FirstOrDefaultAsync(r => r.RoomNo == roomNo);
        if (room is null) return;
        room.Status = WardRoomStatuses.Remaining;
        room.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
