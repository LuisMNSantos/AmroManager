using AmroStockManager.Data.Models;
using AmroStockManager.Services;
using AmroStockManager.Tests.Helpers;

namespace AmroStockManager.Tests.Services;

public class ReservationServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly ReservationService _svc;

    // Fixed local times used across tests — within Cozinha's 08:00–22:00 window
    private static readonly DateTime Slot14h = new(2026, 8, 10, 14, 0, 0, DateTimeKind.Local);
    private static readonly DateTime Slot16h = new(2026, 8, 10, 16, 0, 0, DateTimeKind.Local);
    private static readonly DateTime Slot16h30 = new(2026, 8, 10, 16, 30, 0, DateTimeKind.Local);
    private static readonly DateTime Slot18h = new(2026, 8, 10, 18, 0, 0, DateTimeKind.Local);

    public ReservationServiceTests() => _svc = new ReservationService(_factory);

    // ── CreateAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidSlot_ReturnsSuccess()
    {
        var (ok, error, result) = await _svc.CreateAsync(
            ReservationSpace.Cozinha, "101", "Staff", Slot14h, Slot16h, null);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateAsync_SameSpaceSameTime_ReturnsConflictError()
    {
        await _svc.CreateAsync(ReservationSpace.Cozinha, "101", "Staff", Slot14h, Slot16h, null);

        var (ok, error, _) = await _svc.CreateAsync(
            ReservationSpace.Cozinha, "202", "Staff", Slot14h, Slot16h, null);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("já existe", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_SameTimeButDifferentSpace_Succeeds()
    {
        await _svc.CreateAsync(ReservationSpace.Cozinha, "101", "Staff", Slot14h, Slot16h, null);

        var (ok, _, _) = await _svc.CreateAsync(
            ReservationSpace.Cinema, "101", "Staff", Slot14h, Slot16h, null);

        Assert.True(ok);
    }

    [Fact]
    public async Task CreateAsync_AdjacentSlots_NoConflict()
    {
        // 14:00–16:00 followed immediately by 16:00–18:00 — edge does not overlap
        await _svc.CreateAsync(ReservationSpace.Cozinha, "101", "Staff", Slot14h, Slot16h, null);

        var (ok, _, _) = await _svc.CreateAsync(
            ReservationSpace.Cozinha, "202", "Staff", Slot16h, Slot18h, null);

        Assert.True(ok);
    }

    [Fact]
    public async Task CreateAsync_PartialOverlap_ReturnsConflictError()
    {
        // First slot: 14:00–16:30. Second: 16:00–18:00 — overlaps by 30 min.
        await _svc.CreateAsync(ReservationSpace.Cozinha, "101", "Staff", Slot14h, Slot16h30, null);

        var (ok, _, _) = await _svc.CreateAsync(
            ReservationSpace.Cozinha, "202", "Staff", Slot16h, Slot18h, null);

        Assert.False(ok);
    }

    [Fact]
    public async Task CreateAsync_CancelledConflictingSlot_DoesNotBlock()
    {
        // A cancelled reservation must not block the same slot from being re-booked
        var (_, _, created) = await _svc.CreateAsync(
            ReservationSpace.Cozinha, "101", "Staff", Slot14h, Slot16h, null);
        await _svc.CancelAsync(created!.Id);

        var (ok, _, _) = await _svc.CreateAsync(
            ReservationSpace.Cozinha, "202", "Staff", Slot14h, Slot16h, null);

        Assert.True(ok);
    }

    [Fact]
    public async Task CreateAsync_Cozinha_StartsBefore08_ReturnsError()
    {
        var start = new DateTime(2026, 8, 10, 7, 0, 0, DateTimeKind.Local);
        var end   = new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Local);

        var (ok, error, _) = await _svc.CreateAsync(
            ReservationSpace.Cozinha, "101", "Staff", start, end, null);

        Assert.False(ok);
        Assert.Contains("08:00", error);
    }

    [Fact]
    public async Task CreateAsync_Cozinha_EndsAfter22_ReturnsError()
    {
        var start = new DateTime(2026, 8, 10, 20, 0, 0, DateTimeKind.Local);
        var end   = new DateTime(2026, 8, 10, 23, 0, 0, DateTimeKind.Local);

        var (ok, error, _) = await _svc.CreateAsync(
            ReservationSpace.Cozinha, "101", "Staff", start, end, null);

        Assert.False(ok);
        Assert.Contains("22:00", error);
    }

    // ── CancelAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelAsync_ExistingReservation_SetsCancelledFlag()
    {
        var (_, _, created) = await _svc.CreateAsync(
            ReservationSpace.Cozinha, "101", "Staff", Slot14h, Slot16h, null);

        var ok = await _svc.CancelAsync(created!.Id);

        Assert.True(ok);
        await using var db = _factory.CreateDbContext();
        var stored = await db.Reservations.FindAsync(created.Id);
        Assert.True(stored!.IsCancelled);
    }

    [Fact]
    public async Task CancelAsync_AlreadyCancelled_ReturnsFalse()
    {
        var (_, _, created) = await _svc.CreateAsync(
            ReservationSpace.Cozinha, "101", "Staff", Slot14h, Slot16h, null);
        await _svc.CancelAsync(created!.Id);

        var ok = await _svc.CancelAsync(created.Id);

        Assert.False(ok);
    }

    // ── GetUpcomingByRoomAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetUpcomingByRoomAsync_ReturnsOnlyFutureNonCancelled()
    {
        // Use fixed times within Cozinha's 08:00–22:00 window so tests pass regardless of when they run
        var futureStart = DateTime.Today.AddDays(1).AddHours(10);
        var futureEnd   = futureStart.AddHours(2);
        var pastStart   = DateTime.Today.AddDays(-2).AddHours(10);
        var pastEnd     = pastStart.AddHours(2);

        await _svc.CreateAsync(ReservationSpace.Cozinha, "101", "Staff", futureStart, futureEnd, null);
        await _svc.CreateAsync(ReservationSpace.Cinema,  "101", "Staff", pastStart,   pastEnd,   null);

        var upcoming = await _svc.GetUpcomingByRoomAsync("101");

        Assert.Single(upcoming);
        Assert.Equal(ReservationSpace.Cozinha, upcoming[0].Space);
    }

    [Fact]
    public async Task GetUpcomingByRoomAsync_ExcludesCancelledReservations()
    {
        var futureStart = DateTime.Today.AddDays(1).AddHours(10);
        var futureEnd   = futureStart.AddHours(2);

        var (_, _, created) = await _svc.CreateAsync(
            ReservationSpace.Cozinha, "101", "Staff", futureStart, futureEnd, null);
        await _svc.CancelAsync(created!.Id);

        var upcoming = await _svc.GetUpcomingByRoomAsync("101");

        Assert.Empty(upcoming);
    }

    // ── GetCountsByMonthAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetCountsByMonthAsync_ReturnsCorrectCountPerDay()
    {
        var day10Start = new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Local);
        var day10End   = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Local);
        var day10Start2 = new DateTime(2026, 8, 10, 14, 0, 0, DateTimeKind.Local);
        var day10End2   = new DateTime(2026, 8, 10, 16, 0, 0, DateTimeKind.Local);
        var day15Start = new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Local);
        var day15End   = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Local);

        await _svc.CreateAsync(ReservationSpace.Cozinha, "101", "Staff", day10Start, day10End, null);
        await _svc.CreateAsync(ReservationSpace.Cinema, "102", "Staff", day10Start2, day10End2, null);
        await _svc.CreateAsync(ReservationSpace.Cozinha, "103", "Staff", day15Start, day15End, null);

        var counts = await _svc.GetCountsByMonthAsync(2026, 8);

        Assert.Equal(2, counts[10]);
        Assert.Equal(1, counts[15]);
        Assert.Equal(0, counts.GetValueOrDefault(11));
    }

    public void Dispose() => _factory.Dispose();
}
