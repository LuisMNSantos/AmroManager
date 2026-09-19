using AmroStockManager.Data.Models;
using AmroStockManager.Services;
using NSubstitute;

namespace AmroStockManager.Tests;

public class PendingRegistrationServiceTests
{
    private static PendingRegistration MakeReg(string id = "reg-1", string room = "101A") => new()
    {
        Id          = id,
        Name        = "Ana Costa",
        RoomNumber  = room,
        RequestedAt = DateTime.UtcNow.AddMinutes(-10)
    };

    private static Resident MakeResident(string id = "res-1", string room = "101A") => new()
    {
        Id         = id,
        Name       = "João Silva",
        RoomNumber = room
    };

    // ── GetAllAsync ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_QueriesPendingRegistrationsTable()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);

        db.GetAsync<PendingRegistration>("pending_registrations", Arg.Any<string?>()).Returns([]);

        await svc.GetAllAsync();

        await db.Received(1).GetAsync<PendingRegistration>("pending_registrations", Arg.Any<string?>());
    }

    [Fact]
    public async Task GetAllAsync_OrdersByIdAscending()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);

        db.GetAsync<PendingRegistration>("pending_registrations", Arg.Any<string?>()).Returns([]);

        await svc.GetAllAsync();

        await db.Received(1).GetAsync<PendingRegistration>("pending_registrations",
            Arg.Is<string?>(q => q != null && q.Contains("id.asc")));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllRegistrations()
    {
        var db   = Substitute.For<ISupabaseClient>();
        var svc  = new PendingRegistrationService(db);
        var regs = new List<PendingRegistration> { MakeReg("1"), MakeReg("2") };

        db.GetAsync<PendingRegistration>("pending_registrations", Arg.Any<string?>()).Returns(regs);

        var result = await svc.GetAllAsync();

        Assert.Equal(2, result.Count);
    }

    // ── ApproveAsNewAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsNewAsync_InsertsToResidentsTable()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);

        await svc.ApproveAsNewAsync(MakeReg(), "Ana Costa", "202A", "+351910000001");

        await db.Received(1).InsertAsync<object>("residents", Arg.Any<object>());
    }

    [Fact]
    public async Task ApproveAsNewAsync_DeletesPendingRegistrationById()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);
        var reg = MakeReg("pending-42");

        await svc.ApproveAsNewAsync(reg, "Ana Costa", "202A", null);

        await db.Received(1).DeleteAsync("pending_registrations", "id=eq.pending-42");
    }

    [Fact]
    public async Task ApproveAsNewAsync_NormalizesRoomNumberToUpperCase()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);
        object? captured = null;

        await db.InsertAsync<object>("residents", Arg.Do<object>(b => captured = b));

        await svc.ApproveAsNewAsync(MakeReg(), "Ana Costa", "202a", null);

        Assert.NotNull(captured);
        var room = (string?)captured!.GetType().GetProperty("room_number")?.GetValue(captured);
        Assert.Equal("202A", room);
    }

    [Fact]
    public async Task ApproveAsNewAsync_TrimsNameWhitespace()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);
        object? captured = null;

        await db.InsertAsync<object>("residents", Arg.Do<object>(b => captured = b));

        await svc.ApproveAsNewAsync(MakeReg(), "  Ana Costa  ", "202A", null);

        Assert.NotNull(captured);
        var name = (string?)captured!.GetType().GetProperty("name")?.GetValue(captured);
        Assert.Equal("Ana Costa", name);
    }

    [Fact]
    public async Task ApproveAsNewAsync_TrimsPhoneWhitespace()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);
        object? captured = null;

        await db.InsertAsync<object>("residents", Arg.Do<object>(b => captured = b));

        await svc.ApproveAsNewAsync(MakeReg(), "Ana Costa", "202A", "  +351910000001  ");

        Assert.NotNull(captured);
        var phone = (string?)captured!.GetType().GetProperty("phone_number")?.GetValue(captured);
        Assert.Equal("+351910000001", phone);
    }

    [Fact]
    public async Task ApproveAsNewAsync_WithNullPhone_StoresNull()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);
        object? captured = null;

        await db.InsertAsync<object>("residents", Arg.Do<object>(b => captured = b));

        await svc.ApproveAsNewAsync(MakeReg(), "Ana Costa", "202A", null);

        Assert.NotNull(captured);
        var phone = captured!.GetType().GetProperty("phone_number")?.GetValue(captured);
        Assert.Null(phone);
    }

    [Fact]
    public async Task ApproveAsNewAsync_SetsIsDeletedFalse()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);
        object? captured = null;

        await db.InsertAsync<object>("residents", Arg.Do<object>(b => captured = b));

        await svc.ApproveAsNewAsync(MakeReg(), "Ana Costa", "202A", null);

        Assert.NotNull(captured);
        var isDeleted = (bool?)captured!.GetType().GetProperty("is_deleted")?.GetValue(captured);
        Assert.False(isDeleted);
    }

    // ── ApproveAsReplaceAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsReplaceAsync_PatchesResidentsTable()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);

        await svc.ApproveAsReplaceAsync(MakeReg(), MakeResident(), "João Novo", "303B", null);

        await db.Received(1).PatchAsync("residents", Arg.Any<string>(), Arg.Any<object>());
    }

    [Fact]
    public async Task ApproveAsReplaceAsync_FilterUsesSyncIdOfExistingResident()
    {
        var db       = Substitute.For<ISupabaseClient>();
        var svc      = new PendingRegistrationService(db);
        var existing = MakeResident("existing-99");

        await svc.ApproveAsReplaceAsync(MakeReg(), existing, "João Novo", "303B", null);

        await db.Received(1).PatchAsync(
            "residents",
            "sync_id=eq.existing-99",
            Arg.Any<object>());
    }

    [Fact]
    public async Task ApproveAsReplaceAsync_NormalizesRoomNumberToUpperCase()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);
        object? captured = null;

        await db.PatchAsync("residents", Arg.Any<string>(), Arg.Do<object>(p => captured = p));

        await svc.ApproveAsReplaceAsync(MakeReg(), MakeResident(), "João Novo", "303b", null);

        Assert.NotNull(captured);
        var room = (string?)captured!.GetType().GetProperty("room_number")?.GetValue(captured);
        Assert.Equal("303B", room);
    }

    [Fact]
    public async Task ApproveAsReplaceAsync_TrimsNameWhitespace()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);
        object? captured = null;

        await db.PatchAsync("residents", Arg.Any<string>(), Arg.Do<object>(p => captured = p));

        await svc.ApproveAsReplaceAsync(MakeReg(), MakeResident(), "  João Novo  ", "303B", null);

        Assert.NotNull(captured);
        var name = (string?)captured!.GetType().GetProperty("name")?.GetValue(captured);
        Assert.Equal("João Novo", name);
    }

    [Fact]
    public async Task ApproveAsReplaceAsync_DeletesPendingRegistrationById()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);
        var reg = MakeReg("pending-77");

        await svc.ApproveAsReplaceAsync(reg, MakeResident(), "João Novo", "303B", null);

        await db.Received(1).DeleteAsync("pending_registrations", "id=eq.pending-77");
    }

    // ── RejectAsync ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RejectAsync_DeletesFromPendingRegistrationsTable()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);

        await svc.RejectAsync("some-id");

        await db.Received(1).DeleteAsync("pending_registrations", Arg.Any<string>());
    }

    [Fact]
    public async Task RejectAsync_UsesCorrectIdFilter()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new PendingRegistrationService(db);

        await svc.RejectAsync("abc-123");

        await db.Received(1).DeleteAsync("pending_registrations", "id=eq.abc-123");
    }
}
