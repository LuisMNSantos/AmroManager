using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class RoomService(ISupabaseClient db, CacheService cache)
{
    private const string CacheKey = "rooms_all";

    public Task<Dictionary<string, Room>> GetAllAsync() =>
        cache.GetOrFetchAsync(CacheKey,
            async () =>
            {
                var rows = await db.GetAsync<Room>("rooms", "order=number.asc");
                return rows.ToDictionary(r => r.Number, StringComparer.OrdinalIgnoreCase);
            },
            TimeSpan.FromHours(1));

    public async Task<Room?> GetAsync(string? roomNumber)
    {
        if (string.IsNullOrEmpty(roomNumber)) return null;
        var all = await GetAllAsync();
        return all.TryGetValue(roomNumber, out var r) ? r : null;
    }

    public async Task<IReadOnlyList<string>> GetAllNumbersAsync()
    {
        var all = await GetAllAsync();
        return [.. all.Keys];
    }

    public async Task SetVisitPinAsync(string roomNumber, string pin)
    {
        var room = Uri.EscapeDataString(roomNumber.Trim());
        await db.PatchAsync("rooms", $"number=eq.{room}", new { visit_pin = pin }, verifyAffected: true);
        InvalidateCache();
    }

    public async Task<int> SeedAllPinsAsync()
    {
        var rooms = await db.GetAsync<Room>("rooms", "order=number.asc");
        int count = 0;
        foreach (var room in rooms)
        {
            if (!string.IsNullOrEmpty(room.VisitPin)) continue;
            var pin     = Random.Shared.Next(1000, 9999).ToString();
            var encoded = Uri.EscapeDataString(room.Number.Trim());
            await db.PatchAsync("rooms", $"number=eq.{encoded}", new { visit_pin = pin }, verifyAffected: true);
            count++;
        }
        InvalidateCache();
        return count;
    }

    public void InvalidateCache() => cache.Invalidate(CacheKey);
}
