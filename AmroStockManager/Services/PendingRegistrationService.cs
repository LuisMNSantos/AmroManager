using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class PendingRegistrationService(ISupabaseClient db)
{
    public Task<List<PendingRegistration>> GetAllAsync() =>
        db.GetAsync<PendingRegistration>("pending_registrations",
            "order=requested_at.asc");

    public async Task ApproveAsNewAsync(PendingRegistration reg, string name, string roomNumber, string? phone)
    {
        var now = DateTime.UtcNow;
        await db.InsertAsync<object>("residents", new
        {
            sync_id      = Guid.NewGuid().ToString(),
            name         = name.Trim(),
            room_number  = roomNumber.Trim().ToUpper(),
            phone_number = phone?.Trim(),
            is_deleted   = false,
            updated_at   = now
        });
        await db.DeleteAsync("pending_registrations", $"id=eq.{reg.Id}");
    }

    public async Task ApproveAsReplaceAsync(PendingRegistration reg, Resident existing, string name, string roomNumber, string? phone)
    {
        var now = DateTime.UtcNow;
        await db.PatchAsync("residents", $"sync_id=eq.{existing.Id}", new
        {
            name         = name.Trim(),
            room_number  = roomNumber.Trim().ToUpper(),
            phone_number = phone?.Trim(),
            updated_at   = now
        });
        await db.DeleteAsync("pending_registrations", $"id=eq.{reg.Id}");
    }

    public Task RejectAsync(string id) =>
        db.DeleteAsync("pending_registrations", $"id=eq.{id}");
}
