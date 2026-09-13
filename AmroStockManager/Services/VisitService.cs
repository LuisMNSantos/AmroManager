using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class VisitService(ISupabaseClient db)
{
    public Task<List<Visit>> GetAllAsync() =>
        db.GetAsync<Visit>("visits", "is_deleted=eq.false&order=checked_in_at.desc&limit=300");

    public Task<List<Visit>> GetByRoomAsync(string roomNumber) =>
        db.GetAsync<Visit>("visits",
            $"is_deleted=eq.false&room_number=eq.{Uri.EscapeDataString(roomNumber.Trim().ToUpper())}&order=checked_in_at.desc");

    public async Task<Visit?> CreateAsync(
        string visitorName, string roomNumber, string? registeredBy, string? notes)
    {
        var now = DateTime.UtcNow;
        return await db.InsertAsync<Visit>("visits", new
        {
            sync_id       = Guid.NewGuid().ToString(),
            visitor_name  = visitorName.Trim(),
            room_number   = roomNumber.Trim().ToUpper(),
            registered_by = string.IsNullOrWhiteSpace(registeredBy) ? (string?)null : registeredBy.Trim(),
            checked_in_at = now,
            overnights    = 0,
            notes         = string.IsNullOrWhiteSpace(notes) ? (string?)null : notes.Trim(),
            is_deleted    = false,
            created_at    = now,
            updated_at    = now
        });
    }

    public async Task CheckOutAsync(string visitId)
    {
        var list = await db.GetAsync<Visit>("visits",
            $"sync_id=eq.{visitId}&is_deleted=eq.false&select=sync_id,checked_in_at");
        if (list is not [var visit]) return;

        var now        = DateTime.UtcNow;
        var overnights = Visit.ComputeOvernights(visit.CheckedInAt, now);

        await db.PatchAsync("visits", $"sync_id=eq.{visitId}", new
        {
            checked_out_at = now,
            overnights     = overnights,
            updated_at     = now
        });
    }

    public async Task UpdateAsync(string visitId,
        string visitorName, string roomNumber,
        DateTime checkedInAt, DateTime? checkedOutAt,
        string? registeredBy, string? notes)
    {
        var inUtc = checkedInAt.Kind == DateTimeKind.Utc
            ? checkedInAt : checkedInAt.ToUniversalTime();

        DateTime? outUtc = checkedOutAt.HasValue
            ? (checkedOutAt.Value.Kind == DateTimeKind.Utc
                ? checkedOutAt.Value
                : checkedOutAt.Value.ToUniversalTime())
            : null;

        var overnights = outUtc.HasValue ? Visit.ComputeOvernights(inUtc, outUtc.Value) : 0;

        await db.PatchAsync("visits", $"sync_id=eq.{visitId}", new
        {
            visitor_name   = visitorName.Trim(),
            room_number    = roomNumber.Trim().ToUpper(),
            registered_by  = string.IsNullOrWhiteSpace(registeredBy) ? (string?)null : registeredBy.Trim(),
            checked_in_at  = inUtc,
            checked_out_at = outUtc,
            overnights     = overnights,
            notes          = string.IsNullOrWhiteSpace(notes) ? (string?)null : notes.Trim(),
            updated_at     = DateTime.UtcNow
        });
    }

    public Task DeleteAsync(string visitId) =>
        db.PatchAsync("visits", $"sync_id=eq.{visitId}", new
        {
            is_deleted = true,
            updated_at = DateTime.UtcNow
        });
}
