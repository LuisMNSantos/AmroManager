using AmroStockManager.Data.Models;
using AmroStockManager.Services;
using NSubstitute;

namespace AmroStockManager.Tests;

public class AuditLogServiceTests
{
    // ── LogAsync ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LogAsync_CallsInsertOnAuditLogsTable()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new AuditLogService(db);

        await svc.LogAsync("Residente adicionado");

        await db.Received(1).InsertAsync<AuditLog>("audit_logs", Arg.Any<object>());
    }

    [Fact]
    public async Task LogAsync_PassesActionInBody()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new AuditLogService(db);
        object? captured = null;

        await db.InsertAsync<AuditLog>("audit_logs", Arg.Do<object>(b => captured = b));

        await svc.LogAsync("Residente eliminado", "Maria Costa (#203)");

        Assert.NotNull(captured);
        var action = (string?)captured!.GetType().GetProperty("action")?.GetValue(captured);
        Assert.Equal("Residente eliminado", action);
    }

    [Fact]
    public async Task LogAsync_PassesDetailsInBody()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new AuditLogService(db);
        object? captured = null;

        await db.InsertAsync<AuditLog>("audit_logs", Arg.Do<object>(b => captured = b));

        await svc.LogAsync("BIS entregue", "Quarto #303 por Luís");

        Assert.NotNull(captured);
        var details = (string?)captured!.GetType().GetProperty("details")?.GetValue(captured);
        Assert.Equal("Quarto #303 por Luís", details);
    }

    [Fact]
    public async Task LogAsync_WithNullDetails_StillCallsInsert()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new AuditLogService(db);

        await svc.LogAsync("PIN alterado");

        await db.Received(1).InsertAsync<AuditLog>("audit_logs", Arg.Any<object>());
    }

    [Fact]
    public async Task LogAsync_WithNullDetails_PassesNullInBody()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new AuditLogService(db);
        object? captured = null;

        await db.InsertAsync<AuditLog>("audit_logs", Arg.Do<object>(b => captured = b));

        await svc.LogAsync("PIN alterado");

        Assert.NotNull(captured);
        var details = captured!.GetType().GetProperty("details")?.GetValue(captured);
        Assert.Null(details);
    }

    // ── GetAllAsync ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_QueriesAuditLogsTable()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new AuditLogService(db);

        db.GetAsync<AuditLog>("audit_logs", Arg.Any<string?>()).Returns([]);

        await svc.GetAllAsync();

        await db.Received(1).GetAsync<AuditLog>("audit_logs", Arg.Any<string?>());
    }

    [Fact]
    public async Task GetAllAsync_QueriesWithDescendingOrderByPerformedAt()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new AuditLogService(db);

        db.GetAsync<AuditLog>("audit_logs", Arg.Any<string?>()).Returns([]);

        await svc.GetAllAsync();

        await db.Received(1).GetAsync<AuditLog>("audit_logs",
            Arg.Is<string?>(q => q != null && q.Contains("performed_at.desc")));
    }

    [Fact]
    public async Task GetAllAsync_LimitsResultsTo2000()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new AuditLogService(db);

        db.GetAsync<AuditLog>("audit_logs", Arg.Any<string?>()).Returns([]);

        await svc.GetAllAsync();

        await db.Received(1).GetAsync<AuditLog>("audit_logs",
            Arg.Is<string?>(q => q != null && q.Contains("limit=2000")));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsLogsFromDatabase()
    {
        var db   = Substitute.For<ISupabaseClient>();
        var svc  = new AuditLogService(db);
        var logs = new List<AuditLog>
        {
            new() { Id = "1", Action = "Residente adicionado", PerformedAt = DateTime.UtcNow },
            new() { Id = "2", Action = "BIS entregue",         PerformedAt = DateTime.UtcNow.AddMinutes(-5) }
        };

        db.GetAsync<AuditLog>("audit_logs", Arg.Any<string?>()).Returns(logs);

        var result = await svc.GetAllAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Residente adicionado", result[0].Action);
    }

    [Fact]
    public async Task GetAllAsync_WhenTableIsEmpty_ReturnsEmptyList()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new AuditLogService(db);

        db.GetAsync<AuditLog>("audit_logs", Arg.Any<string?>()).Returns([]);

        var result = await svc.GetAllAsync();

        Assert.Empty(result);
    }
}
