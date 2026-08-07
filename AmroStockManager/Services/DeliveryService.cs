using Microsoft.EntityFrameworkCore;
using AmroStockManager.Data;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public record DeliveryDashboardStats(
    int PendingPackages,
    int PendingLetters,
    int ArrivedToday,
    int CollectedToday);

public class DeliveryService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Delivery>> GetPendingAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Deliveries
            .Where(d => d.CollectedAt == null)
            .OrderByDescending(d => d.ArrivedAt)
            .ToListAsync();
    }

    public async Task<List<Delivery>> GetHistoryAsync(int limit = 100)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Deliveries
            .Where(d => d.CollectedAt != null)
            .OrderByDescending(d => d.CollectedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<DeliveryDashboardStats> GetDashboardStatsAsync()
    {
        var todayUtc = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local).ToUniversalTime();
        await using var db = await dbFactory.CreateDbContextAsync();
        return new DeliveryDashboardStats(
            PendingPackages: await db.Deliveries.CountAsync(d => d.CollectedAt == null && d.Type == DeliveryType.Encomenda),
            PendingLetters:  await db.Deliveries.CountAsync(d => d.CollectedAt == null && d.Type == DeliveryType.Carta),
            ArrivedToday:    await db.Deliveries.CountAsync(d => d.ArrivedAt >= todayUtc),
            CollectedToday:  await db.Deliveries.CountAsync(d => d.CollectedAt != null && d.CollectedAt >= todayUtc)
        );
    }

    public async Task<Delivery> RegisterAsync(DeliveryType type, string roomNumber, int quantity, string? notes)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var delivery = new Delivery
        {
            Type       = type,
            RoomNumber = roomNumber.Trim().ToUpper(),
            Quantity   = quantity,
            Notes      = notes,
            ArrivedAt  = DateTime.UtcNow
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery;
    }

    public async Task MarkCollectedAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var delivery = await db.Deliveries.FindAsync(id);
        if (delivery is null || delivery.IsCollected) return;
        delivery.CollectedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var delivery = await db.Deliveries.FindAsync(id);
        if (delivery is not null)
        {
            db.Deliveries.Remove(delivery);
            await db.SaveChangesAsync();
        }
    }
}
