using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public record DeliveryDashboardStats(
    int PendingPackages,
    int PendingLetters,
    int ArrivedToday,
    int CollectedToday);

public class DeliveryService(SupabaseClient db)
{
    public async Task<List<Delivery>> GetPendingAsync()
    {
        return await db.GetAsync<Delivery>("deliveries",
            "is_deleted=eq.false&is_delivered=eq.false&order=arrived_at.asc");
    }

    public async Task<List<Delivery>> GetPendingByRoomAsync(string roomNumber)
    {
        var room = Uri.EscapeDataString(roomNumber.Trim().ToUpper());
        return await db.GetAsync<Delivery>("deliveries",
            $"is_deleted=eq.false&is_delivered=eq.false&room_number=eq.{room}&order=arrived_at.desc");
    }

    public async Task<List<Delivery>> GetHistoryAsync(int limit = 100)
    {
        return await db.GetAsync<Delivery>("deliveries",
            $"is_deleted=eq.false&is_delivered=eq.true&order=collected_at.desc&limit={limit}");
    }

    public async Task<DeliveryDashboardStats> GetDashboardStatsAsync()
    {
        var todayUtc = DateTime.Today.ToUniversalTime();
        var all = await db.GetAsync<Delivery>("deliveries",
            "is_deleted=eq.false&select=type,is_delivered,arrived_at,collected_at");
        return new DeliveryDashboardStats(
            PendingPackages: all.Count(d => !d.IsDelivered && d.Type == DeliveryType.Encomenda),
            PendingLetters:  all.Count(d => !d.IsDelivered && d.Type == DeliveryType.Carta),
            ArrivedToday:    all.Count(d => d.ArrivedAt >= todayUtc),
            CollectedToday:  all.Count(d => d.IsDelivered && d.CollectedAt >= todayUtc)
        );
    }

    public async Task<Delivery> RegisterAsync(DeliveryType type, string roomNumber, int quantity, string? notes)
    {
        var now = DateTime.UtcNow;
        var created = await db.InsertAsync<Delivery>("deliveries", new
        {
            sync_id      = Guid.NewGuid().ToString(),
            type         = (int)type,
            room_number  = roomNumber.Trim().ToUpper(),
            quantity     = quantity,
            notes        = notes,
            arrived_at   = now,
            is_delivered = false,
            is_deleted   = false,
            updated_at   = now
        });
        return created!;
    }

    public async Task MarkCollectedAsync(string id)
    {
        var list = await db.GetAsync<Delivery>("deliveries", $"sync_id=eq.{id}&select=sync_id,is_delivered");
        if (list is not [var d] || d.IsDelivered) return;

        await db.PatchAsync("deliveries", $"sync_id=eq.{id}", new
        {
            collected_at = DateTime.UtcNow,
            is_delivered = true,
            updated_at   = DateTime.UtcNow
        });
    }

    public async Task DeleteAsync(string id)
    {
        await db.PatchAsync("deliveries", $"sync_id=eq.{id}", new { is_deleted = true, updated_at = DateTime.UtcNow });
    }
}
