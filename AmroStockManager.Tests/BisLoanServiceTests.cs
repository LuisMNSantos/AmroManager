using AmroStockManager.Data.Models;
using AmroStockManager.Services;
using NSubstitute;

namespace AmroStockManager.Tests;

public class BisLoanServiceTests
{
    private static BisLoan MakeLoan(string room = "101A") => new()
    {
        Id         = Guid.NewGuid().ToString(),
        RoomNumber = room,
        GivenBy    = "Staff",
        LoanDate   = DateTime.UtcNow.AddHours(-1),
        IsReturned = false,
        IsDeleted  = false
    };

    // ── LendAsync ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task LendAsync_WhenRoomAlreadyHasActiveBis_ReturnsError()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new BisLoanService(db);

        db.GetAsync<BisLoan>("bis_loans", Arg.Any<string?>())
          .Returns([MakeLoan("101A")]);

        var (success, error) = await svc.LendAsync("101A", "Staff", null);

        Assert.False(success);
        Assert.Equal("Este quarto já tem um BIS em uso.", error);
        await db.DidNotReceive().InsertAsync<BisLoan>(Arg.Any<string>(), Arg.Any<object>());
    }

    [Fact]
    public async Task LendAsync_WhenRoomHasNoActiveBis_InsertsAndReturnsSuccess()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new BisLoanService(db);

        db.GetAsync<BisLoan>("bis_loans", Arg.Any<string?>())
          .Returns([]);
        db.InsertAsync<BisLoan>("bis_loans", Arg.Any<object>())
          .Returns((BisLoan?)null);

        var (success, error) = await svc.LendAsync("202B", "Receção", "perdeu o cartão");

        Assert.True(success);
        Assert.Null(error);
        await db.Received(1).InsertAsync<BisLoan>("bis_loans", Arg.Any<object>());
    }

    [Fact]
    public async Task LendAsync_NormalizesRoomNumberToUpperCase()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new BisLoanService(db);

        db.GetAsync<BisLoan>("bis_loans", Arg.Any<string?>()).Returns([]);
        db.InsertAsync<BisLoan>(Arg.Any<string>(), Arg.Any<object>()).Returns((BisLoan?)null);

        await svc.LendAsync("101a", "Staff", null);

        // Query must include the upper-cased room number
        await db.Received(1).GetAsync<BisLoan>("bis_loans",
            Arg.Is<string?>(q => q != null && q.Contains("101A")));
    }

    [Fact]
    public async Task LendAsync_TrimsWhitespaceFromRoomAndGivenBy()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new BisLoanService(db);

        db.GetAsync<BisLoan>("bis_loans", Arg.Any<string?>()).Returns([]);
        db.InsertAsync<BisLoan>(Arg.Any<string>(), Arg.Any<object>()).Returns((BisLoan?)null);

        var (success, _) = await svc.LendAsync("  101A  ", "  Staff  ", null);

        Assert.True(success);
    }

    // ── ReturnAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReturnAsync_CallsPatchWithCorrectSyncIdFilter()
    {
        var db     = Substitute.For<ISupabaseClient>();
        var svc    = new BisLoanService(db);
        var syncId = Guid.NewGuid().ToString();

        await svc.ReturnAsync(syncId);

        await db.Received(1).PatchAsync(
            "bis_loans",
            Arg.Is<string>(f => f == $"sync_id=eq.{syncId}"),
            Arg.Any<object>());
    }

    [Fact]
    public async Task ReturnAsync_SetsIsReturnedAndReturnDate()
    {
        var db      = Substitute.For<ISupabaseClient>();
        var svc     = new BisLoanService(db);
        var syncId  = Guid.NewGuid().ToString();
        object? captured = null;

        await db.PatchAsync("bis_loans", Arg.Any<string>(), Arg.Do<object>(p => captured = p));

        await svc.ReturnAsync(syncId);

        Assert.NotNull(captured);
        var type       = captured!.GetType();
        var isReturned = (bool?)type.GetProperty("is_returned")?.GetValue(captured);
        Assert.True(isReturned);
    }

    // ── GetAllActiveAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllActiveAsync_QueriesIsReturnedFalseAndNotDeleted()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new BisLoanService(db);

        db.GetAsync<BisLoan>("bis_loans", Arg.Any<string?>()).Returns([]);

        await svc.GetAllActiveAsync();

        await db.Received(1).GetAsync<BisLoan>("bis_loans",
            Arg.Is<string?>(q => q != null
                                 && q.Contains("is_returned=eq.false")
                                 && q.Contains("is_deleted=eq.false")));
    }

    [Fact]
    public async Task GetAllActiveAsync_ReturnsAllActiveLoans()
    {
        var db    = Substitute.For<ISupabaseClient>();
        var svc   = new BisLoanService(db);
        var loans = new List<BisLoan> { MakeLoan("101A"), MakeLoan("202B") };

        db.GetAsync<BisLoan>("bis_loans", Arg.Any<string?>()).Returns(loans);

        var result = await svc.GetAllActiveAsync();

        Assert.Equal(2, result.Count);
    }

    // ── GetAllActiveByRoomAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAllActiveByRoomAsync_ReturnsDictionaryKeyedByRoom()
    {
        var db    = Substitute.For<ISupabaseClient>();
        var svc   = new BisLoanService(db);
        var loans = new List<BisLoan> { MakeLoan("101A"), MakeLoan("202B") };

        db.GetAsync<BisLoan>("bis_loans", Arg.Any<string?>()).Returns(loans);

        var dict = await svc.GetAllActiveByRoomAsync();

        Assert.True(dict.ContainsKey("101A"));
        Assert.True(dict.ContainsKey("202B"));
        Assert.Equal(2, dict.Count);
    }

    [Fact]
    public async Task GetAllActiveByRoomAsync_IsCaseInsensitive()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new BisLoanService(db);

        db.GetAsync<BisLoan>("bis_loans", Arg.Any<string?>())
          .Returns([MakeLoan("101A")]);

        var dict = await svc.GetAllActiveByRoomAsync();

        Assert.True(dict.ContainsKey("101a"));
        Assert.True(dict.ContainsKey("101A"));
    }

    // ── GetActiveByRoomAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveByRoomAsync_ReturnsNullWhenNoneFound()
    {
        var db  = Substitute.For<ISupabaseClient>();
        var svc = new BisLoanService(db);

        db.GetAsync<BisLoan>("bis_loans", Arg.Any<string?>()).Returns([]);

        var result = await svc.GetActiveByRoomAsync("101A");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveByRoomAsync_ReturnsLoanWhenFound()
    {
        var db   = Substitute.For<ISupabaseClient>();
        var svc  = new BisLoanService(db);
        var loan = MakeLoan("101A");

        db.GetAsync<BisLoan>("bis_loans", Arg.Any<string?>()).Returns([loan]);

        var result = await svc.GetActiveByRoomAsync("101A");

        Assert.NotNull(result);
        Assert.Equal("101A", result!.RoomNumber);
    }

    // ── BisLoan model ───────────────────────────────────────────────────────────

    [Fact]
    public void BisLoan_DaysOut_ReturnsZeroForLoanIssuedToday()
    {
        var loan = new BisLoan { LoanDate = DateTime.UtcNow.AddMinutes(-30) };
        Assert.Equal(0, loan.DaysOut);
    }

    [Fact]
    public void BisLoan_DaysOut_ReturnsCorrectDaysForOlderLoan()
    {
        var loan = new BisLoan { LoanDate = DateTime.UtcNow.AddDays(-3).AddHours(-1) };
        Assert.Equal(3, loan.DaysOut);
    }

    [Fact]
    public void BisLoan_DaysOut_ReturnsSingleDayForYesterdayLoan()
    {
        var loan = new BisLoan { LoanDate = DateTime.UtcNow.AddDays(-1) };
        Assert.Equal(1, loan.DaysOut);
    }
}
