using AmroStockManager.Services;
using NSubstitute;

namespace AmroStockManager.Tests;

public class ResidentServiceTests
{
    private static ResidentService MakeSvc(ISupabaseClient db) =>
        new(db, new CacheService());

    // Reflection helper — keeps null-conditionals out of expression-tree lambdas
    private static string? PropStr(object b, string name) =>
        b.GetType().GetProperty(name)?.GetValue(b) as string;

    // ── ParseRoomNumber ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("#101",              "101")]
    [InlineData("#101 - João Silva", "101")]
    [InlineData("101",               "101")]
    [InlineData("101a",              "101A")]
    [InlineData("  101  ",           "101")]
    [InlineData("#404B - Maria",     "404B")]
    public void ParseRoomNumber_VariousFormats_ReturnsNormalisedRoom(string input, string expected) =>
        Assert.Equal(expected, ResidentService.ParseRoomNumber(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseRoomNumber_EmptyOrNull_ReturnsEmptyString(string? input) =>
        Assert.Equal(string.Empty, ResidentService.ParseRoomNumber(input));

    // ── SwapRoomsAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SwapRoomsAsync_WhenRoomsAreEqual_ThrowsInvalidOperationException()
    {
        var svc = MakeSvc(Substitute.For<ISupabaseClient>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SwapRoomsAsync("101", "101"));
    }

    [Fact]
    public async Task SwapRoomsAsync_WhenRoomsEqualAfterNormalization_ThrowsInvalidOperationException()
    {
        var svc = MakeSvc(Substitute.For<ISupabaseClient>());

        // lowercase + whitespace still resolves to the same room after Trim().ToUpper()
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SwapRoomsAsync("  101a  ", "101A"));
    }

    [Fact]
    public async Task SwapRoomsAsync_DoesNotCallDatabaseWhenRoomsAreEqual()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = MakeSvc(db);

        try { await svc.SwapRoomsAsync("101", "101"); } catch { }

        await db.DidNotReceive().PatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object>());
    }

    [Fact]
    public async Task SwapRoomsAsync_PatchesResidentsTableExactlyThreeTimes()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = MakeSvc(db);

        await svc.SwapRoomsAsync("101", "202");

        await db.Received(3).PatchAsync("residents", Arg.Any<string>(), Arg.Any<object>());
    }

    [Fact]
    public async Task SwapRoomsAsync_PatchesAllRelatedTablesThreeTimes()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = MakeSvc(db);

        await svc.SwapRoomsAsync("101", "202");

        string[] related =
        [
            "deliveries", "visits", "bis_loans", "general_item_loans",
            "reservations", "renewer_kit_deliveries", "reimbursements"
        ];

        foreach (var table in related)
            await db.Received(3).PatchAsync(table, Arg.Any<string>(), Arg.Any<object>());
    }

    [Fact]
    public async Task SwapRoomsAsync_StepOne_AssignsTemporaryKeyStartingWithSWAP()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = MakeSvc(db);

        await svc.SwapRoomsAsync("101", "202");

        // Step 1: filter on roomA ("101"), body contains a SWAP… temp key
        await db.Received(1).PatchAsync(
            "residents",
            Arg.Is<string>(f => f.Contains("eq.101")),
            Arg.Is<object>(b => (PropStr(b, "room_number") ?? "").StartsWith("SWAP")));
    }

    [Fact]
    public async Task SwapRoomsAsync_StepTwo_MovesRoomBToRoomA()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = MakeSvc(db);

        await svc.SwapRoomsAsync("101", "202");

        // Step 2: filter on roomB ("202"), body sets room_number to roomA ("101")
        await db.Received(1).PatchAsync(
            "residents",
            Arg.Is<string>(f => f.Contains("eq.202")),
            Arg.Is<object>(b => PropStr(b, "room_number") == "101"));
    }

    [Fact]
    public async Task SwapRoomsAsync_StepThree_MovesTempToRoomB()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = MakeSvc(db);

        await svc.SwapRoomsAsync("101", "202");

        // Step 3: filter contains the SWAP temp key, body sets room_number to roomB ("202")
        await db.Received(1).PatchAsync(
            "residents",
            Arg.Is<string>(f => f.Contains("SWAP")),
            Arg.Is<object>(b => PropStr(b, "room_number") == "202"));
    }

    [Fact]
    public async Task SwapRoomsAsync_NormalizesRoomNumbersToUpperCase()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = MakeSvc(db);

        await svc.SwapRoomsAsync("101a", "202b");

        // Both room numbers must be uppercased in the DB filters
        await db.Received(1).PatchAsync(
            "residents",
            Arg.Is<string>(f => f.Contains("eq.101A")),
            Arg.Any<object>());

        await db.Received(1).PatchAsync(
            "residents",
            Arg.Is<string>(f => f.Contains("eq.202B")),
            Arg.Any<object>());
    }
}
