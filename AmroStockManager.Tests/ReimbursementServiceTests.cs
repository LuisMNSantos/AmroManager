using AmroStockManager.Data.Models;
using AmroStockManager.Services;
using NSubstitute;

namespace AmroStockManager.Tests;

public class ReimbursementServiceTests
{
    private static Reimbursement MakeReimbursement(string id = "abc123") => new()
    {
        Id           = id,
        ResidentName = "João Silva",
        RoomNumber   = "101",
        Amount       = 5.50m,
        IsPaid       = false,
        IsDeleted    = false,
        CreatedAt    = DateTime.UtcNow
    };

    // ── GetPendingAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingAsync_QueriesReimbursementsTable()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);

        db.GetAsync<Reimbursement>("reimbursements", Arg.Any<string?>()).Returns([]);

        await svc.GetPendingAsync();

        await db.Received(1).GetAsync<Reimbursement>("reimbursements", Arg.Any<string?>());
    }

    [Fact]
    public async Task GetPendingAsync_FiltersDeletedAndPaidRecords()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);

        db.GetAsync<Reimbursement>("reimbursements", Arg.Any<string?>()).Returns([]);

        await svc.GetPendingAsync();

        await db.Received(1).GetAsync<Reimbursement>("reimbursements",
            Arg.Is<string?>(q => q != null
                                 && q.Contains("is_deleted=eq.false")
                                 && q.Contains("is_paid=eq.false")));
    }

    [Fact]
    public async Task GetPendingAsync_OrdersByCreatedAtDescending()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);

        db.GetAsync<Reimbursement>("reimbursements", Arg.Any<string?>()).Returns([]);

        await svc.GetPendingAsync();

        await db.Received(1).GetAsync<Reimbursement>("reimbursements",
            Arg.Is<string?>(q => q != null && q.Contains("order=created_at.desc")));
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsPendingReimbursements()
    {
        var db   = Substitute.For<ISupabaseClient>();
        var svc  = new ReimbursementService(db);
        var list = new List<Reimbursement> { MakeReimbursement("1"), MakeReimbursement("2") };

        db.GetAsync<Reimbursement>("reimbursements", Arg.Any<string?>()).Returns(list);

        var result = await svc.GetPendingAsync();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetPendingAsync_WhenNoPending_ReturnsEmptyList()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);

        db.GetAsync<Reimbursement>("reimbursements", Arg.Any<string?>()).Returns([]);

        var result = await svc.GetPendingAsync();

        Assert.Empty(result);
    }

    // ── AddAsync ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_InsertsIntoReimbursementsTable()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);

        await svc.AddAsync("João Silva", "101", 5.50m, null);

        await db.Received(1).InsertAsync<Reimbursement>("reimbursements", Arg.Any<object>());
    }

    [Fact]
    public async Task AddAsync_NormalizesRoomNumberToUpperCase()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);
        object? captured = null;

        await db.InsertAsync<Reimbursement>("reimbursements", Arg.Do<object>(b => captured = b));

        await svc.AddAsync("João Silva", "101a", 5.50m, null);

        var room = (string?)captured!.GetType().GetProperty("room_number")?.GetValue(captured);
        Assert.Equal("101A", room);
    }

    [Fact]
    public async Task AddAsync_TrimsResidentName()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);
        object? captured = null;

        await db.InsertAsync<Reimbursement>("reimbursements", Arg.Do<object>(b => captured = b));

        await svc.AddAsync("  João Silva  ", "101", 5.50m, null);

        var name = (string?)captured!.GetType().GetProperty("resident_name")?.GetValue(captured);
        Assert.Equal("João Silva", name);
    }

    [Fact]
    public async Task AddAsync_StoresNullWhenReasonIsEmpty()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);
        object? captured = null;

        await db.InsertAsync<Reimbursement>("reimbursements", Arg.Do<object>(b => captured = b));

        await svc.AddAsync("João", "101", 5m, "");

        var reason = captured!.GetType().GetProperty("reason")?.GetValue(captured);
        Assert.Null(reason);
    }

    [Fact]
    public async Task AddAsync_StoresNullWhenReasonIsWhitespace()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);
        object? captured = null;

        await db.InsertAsync<Reimbursement>("reimbursements", Arg.Do<object>(b => captured = b));

        await svc.AddAsync("João", "101", 5m, "   ");

        var reason = captured!.GetType().GetProperty("reason")?.GetValue(captured);
        Assert.Null(reason);
    }

    [Fact]
    public async Task AddAsync_TrimsAndStoresReason()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);
        object? captured = null;

        await db.InsertAsync<Reimbursement>("reimbursements", Arg.Do<object>(b => captured = b));

        await svc.AddAsync("João", "101", 5m, "  Falha na máquina  ");

        var reason = (string?)captured!.GetType().GetProperty("reason")?.GetValue(captured);
        Assert.Equal("Falha na máquina", reason);
    }

    [Fact]
    public async Task AddAsync_SetsIsPaidFalseAndIsDeletedFalse()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);
        object? captured = null;

        await db.InsertAsync<Reimbursement>("reimbursements", Arg.Do<object>(b => captured = b));

        await svc.AddAsync("João", "101", 5m, null);

        var isPaid    = (bool?)captured!.GetType().GetProperty("is_paid")?.GetValue(captured);
        var isDeleted = (bool?)captured!.GetType().GetProperty("is_deleted")?.GetValue(captured);
        Assert.False(isPaid);
        Assert.False(isDeleted);
    }

    // ── MarkAsPaidAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkAsPaidAsync_PatchesWithCorrectSyncIdFilter()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);
        var id  = Guid.NewGuid().ToString();

        await svc.MarkAsPaidAsync(id);

        await db.Received(1).PatchAsync(
            "reimbursements",
            Arg.Is<string>(f => f == $"sync_id=eq.{id}"),
            Arg.Any<object>());
    }

    [Fact]
    public async Task MarkAsPaidAsync_SetsIsPaidToTrue()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);
        object? captured = null;

        await db.PatchAsync("reimbursements", Arg.Any<string>(), Arg.Do<object>(b => captured = b));

        await svc.MarkAsPaidAsync("some-id");

        var isPaid = (bool?)captured!.GetType().GetProperty("is_paid")?.GetValue(captured);
        Assert.True(isPaid);
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_PatchesWithCorrectSyncIdFilter()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);
        var id  = Guid.NewGuid().ToString();

        await svc.DeleteAsync(id);

        await db.Received(1).PatchAsync(
            "reimbursements",
            Arg.Is<string>(f => f == $"sync_id=eq.{id}"),
            Arg.Any<object>());
    }

    [Fact]
    public async Task DeleteAsync_SetsIsDeletedToTrue()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new ReimbursementService(db);
        object? captured = null;

        await db.PatchAsync("reimbursements", Arg.Any<string>(), Arg.Do<object>(b => captured = b));

        await svc.DeleteAsync("some-id");

        var isDeleted = (bool?)captured!.GetType().GetProperty("is_deleted")?.GetValue(captured);
        Assert.True(isDeleted);
    }
}
