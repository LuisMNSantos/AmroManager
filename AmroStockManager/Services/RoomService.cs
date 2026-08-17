using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class RoomService(SupabaseClient db, CacheService cache)
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

    public void InvalidateCache() => cache.Invalidate(CacheKey);
}
