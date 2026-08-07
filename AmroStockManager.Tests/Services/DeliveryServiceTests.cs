using AmroStockManager.Data.Models;
using AmroStockManager.Services;
using AmroStockManager.Tests.Helpers;

namespace AmroStockManager.Tests.Services;

public class DeliveryServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly DeliveryService _svc;

    public DeliveryServiceTests() => _svc = new DeliveryService(_factory);

    // ── RegisterAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_CreatesDelivery()
    {
        var delivery = await _svc.RegisterAsync(DeliveryType.Encomenda, "101", 1, null);

        Assert.NotEqual(0, delivery.Id);
        Assert.Equal("101", delivery.RoomNumber);
        Assert.Equal(DeliveryType.Encomenda, delivery.Type);
        Assert.Null(delivery.CollectedAt);
    }

    [Fact]
    public async Task RegisterAsync_NormalisesRoomToUppercase()
    {
        var delivery = await _svc.RegisterAsync(DeliveryType.Carta, "101a", 1, null);

        Assert.Equal("101A", delivery.RoomNumber);
    }

    // ── MarkCollectedAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task MarkCollectedAsync_SetsCollectedAt()
    {
        var delivery = await _svc.RegisterAsync(DeliveryType.Encomenda, "101", 1, null);

        await _svc.MarkCollectedAsync(delivery.Id);

        await using var db = _factory.CreateDbContext();
        var stored = await db.Deliveries.FindAsync(delivery.Id);
        Assert.NotNull(stored!.CollectedAt);
        Assert.True(stored.IsCollected);
    }

    [Fact]
    public async Task MarkCollectedAsync_AlreadyCollected_DoesNotChangeTimestamp()
    {
        var delivery = await _svc.RegisterAsync(DeliveryType.Encomenda, "101", 1, null);
        await _svc.MarkCollectedAsync(delivery.Id);

        await using var db = _factory.CreateDbContext();
        var firstTimestamp = (await db.Deliveries.FindAsync(delivery.Id))!.CollectedAt;

        await _svc.MarkCollectedAsync(delivery.Id);
        var secondTimestamp = (await db.Deliveries.FindAsync(delivery.Id))!.CollectedAt;

        Assert.Equal(firstTimestamp, secondTimestamp);
    }

    // ── GetPendingAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingAsync_ReturnsOnlyUncollected()
    {
        var d1 = await _svc.RegisterAsync(DeliveryType.Encomenda, "101", 1, null);
        var d2 = await _svc.RegisterAsync(DeliveryType.Carta, "202", 1, null);
        await _svc.MarkCollectedAsync(d1.Id); // collected — should not appear

        var pending = await _svc.GetPendingAsync();

        Assert.Single(pending);
        Assert.Equal(d2.Id, pending[0].Id);
    }

    [Fact]
    public async Task GetPendingAsync_EmptyDatabase_ReturnsEmpty()
    {
        var pending = await _svc.GetPendingAsync();
        Assert.Empty(pending);
    }

    // ── GetPendingByRoomAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingByRoomAsync_FiltersByRoom()
    {
        await _svc.RegisterAsync(DeliveryType.Encomenda, "101", 1, null);
        await _svc.RegisterAsync(DeliveryType.Carta, "202", 1, null);

        var pending101 = await _svc.GetPendingByRoomAsync("101");
        var pending202 = await _svc.GetPendingByRoomAsync("202");

        Assert.Single(pending101);
        Assert.Single(pending202);
        Assert.Equal("101", pending101[0].RoomNumber);
    }

    [Fact]
    public async Task GetPendingByRoomAsync_CaseInsensitive_FindsDeliveries()
    {
        await _svc.RegisterAsync(DeliveryType.Encomenda, "101A", 1, null);

        var pending = await _svc.GetPendingByRoomAsync("101a");

        Assert.Single(pending);
    }

    [Fact]
    public async Task GetPendingByRoomAsync_ExcludesCollected()
    {
        var d = await _svc.RegisterAsync(DeliveryType.Encomenda, "101", 1, null);
        await _svc.MarkCollectedAsync(d.Id);

        var pending = await _svc.GetPendingByRoomAsync("101");

        Assert.Empty(pending);
    }

    // ── GetHistoryAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistoryAsync_ReturnsOnlyCollected()
    {
        var d1 = await _svc.RegisterAsync(DeliveryType.Encomenda, "101", 1, null);
        await _svc.RegisterAsync(DeliveryType.Carta, "202", 1, null); // pending — not in history
        await _svc.MarkCollectedAsync(d1.Id);

        var history = await _svc.GetHistoryAsync();

        Assert.Single(history);
        Assert.Equal(d1.Id, history[0].Id);
    }

    public void Dispose() => _factory.Dispose();
}
