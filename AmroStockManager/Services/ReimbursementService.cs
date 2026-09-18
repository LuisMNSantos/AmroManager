using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class ReimbursementService(ISupabaseClient db)
{
    public Task<List<Reimbursement>> GetPendingAsync() =>
        db.GetAsync<Reimbursement>("reimbursements",
            "is_deleted=eq.false&is_paid=eq.false&order=created_at.desc");

    public async Task AddAsync(string residentName, string roomNumber, decimal amount, string? reason)
    {
        var now = DateTime.UtcNow;
        await db.InsertAsync<Reimbursement>("reimbursements", new
        {
            sync_id       = Guid.NewGuid().ToString(),
            resident_name = residentName.Trim(),
            room_number   = roomNumber.Trim().ToUpper(),
            amount        = amount,
            reason        = string.IsNullOrWhiteSpace(reason) ? (string?)null : reason.Trim(),
            is_paid       = false,
            is_deleted    = false,
            created_at    = now,
            updated_at    = now
        });
    }

    public Task MarkAsPaidAsync(string id) =>
        db.PatchAsync("reimbursements", $"sync_id=eq.{id}",
            new { is_paid = true, updated_at = DateTime.UtcNow });

    public Task DeleteAsync(string id) =>
        db.PatchAsync("reimbursements", $"sync_id=eq.{id}",
            new { is_deleted = true, updated_at = DateTime.UtcNow });
}
