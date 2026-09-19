using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class BisLoanService(ISupabaseClient db)
{
    public async Task<BisLoan?> GetActiveByRoomAsync(string roomNumber)
    {
        var room = Uri.EscapeDataString(roomNumber.Trim().ToUpper());
        var list = await db.GetAsync<BisLoan>("bis_loans",
            $"room_number=eq.{room}&is_deleted=eq.false&is_returned=eq.false&limit=1");
        return list.FirstOrDefault();
    }

    public Task<List<BisLoan>> GetAllActiveAsync() =>
        db.GetAsync<BisLoan>("bis_loans",
            "is_deleted=eq.false&is_returned=eq.false&order=loan_date.asc");

    public async Task<Dictionary<string, BisLoan>> GetAllActiveByRoomAsync()
    {
        var loans = await GetAllActiveAsync();
        return loans.ToDictionary(l => l.RoomNumber, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<(bool Success, string? Error)> LendAsync(
        string roomNumber, string givenBy, string? notes)
    {
        var existing = await GetActiveByRoomAsync(roomNumber);
        if (existing is not null)
            return (false, "Este quarto já tem um BIS em uso.");

        try
        {
            var now = DateTime.UtcNow;
            await db.InsertAsync<BisLoan>("bis_loans", new
            {
                sync_id     = Guid.NewGuid().ToString(),
                room_number = roomNumber.Trim().ToUpper(),
                given_by    = givenBy.Trim(),
                loan_date   = now,
                is_returned = false,
                is_deleted  = false,
                notes,
                updated_at  = now
            });
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task ReturnAsync(string syncId)
    {
        var now = DateTime.UtcNow;
        await db.PatchAsync("bis_loans", $"sync_id=eq.{syncId}",
            new { return_date = now, is_returned = true, updated_at = now });
    }
}
