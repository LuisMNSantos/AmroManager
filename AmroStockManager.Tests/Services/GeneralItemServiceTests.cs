using AmroStockManager.Services;
using AmroStockManager.Tests.Helpers;

namespace AmroStockManager.Tests.Services;

public class GeneralItemServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly GeneralItemService _svc;

    public GeneralItemServiceTests() => _svc = new GeneralItemService(_factory);

    // ── LendItemAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LendItemAsync_WithAvailableStock_Succeeds()
    {
        var itemId = _factory.SeedGeneralItem("Altifalante", totalQuantity: 1);

        var ok = await _svc.LendItemAsync(itemId, "101", "Staff", null);

        Assert.True(ok);
    }

    [Fact]
    public async Task LendItemAsync_WithNoStock_Fails()
    {
        var itemId = _factory.SeedGeneralItem("Altifalante", totalQuantity: 1);
        await _svc.LendItemAsync(itemId, "101", "Staff", null); // exhausts stock

        var ok = await _svc.LendItemAsync(itemId, "202", "Staff", null);

        Assert.False(ok);
    }

    [Fact]
    public async Task LendItemAsync_ReducesAvailableCount()
    {
        var itemId = _factory.SeedGeneralItem("Estendal", totalQuantity: 2);

        await _svc.LendItemAsync(itemId, "101", "Staff", null);
        await _svc.LendItemAsync(itemId, "202", "Staff", null);

        // Third loan should fail — zero available
        var ok = await _svc.LendItemAsync(itemId, "303", "Staff", null);
        Assert.False(ok);
    }

    [Fact]
    public async Task LendItemAsync_QuantityGreaterThanOne_ConsumesCorrectly()
    {
        var itemId = _factory.SeedGeneralItem("Raquetas", totalQuantity: 4);

        var ok = await _svc.LendItemAsync(itemId, "101", "Staff", null, quantity: 3);

        Assert.True(ok);

        // Only 1 left — lending 2 more should fail
        var fail = await _svc.LendItemAsync(itemId, "202", "Staff", null, quantity: 2);
        Assert.False(fail);
    }

    // ── ReturnItemAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ReturnItemAsync_ActiveLoan_SetsReturnDate()
    {
        var itemId = _factory.SeedGeneralItem("Altifalante", totalQuantity: 1);
        var loanId = await _svc.LendAndGetLoanIdAsync(itemId, "101", "Staff", null);

        await _svc.ReturnItemAsync(loanId!.Value);

        await using var db = _factory.CreateDbContext();
        var loan = await db.GeneralItemLoans.FindAsync(loanId.Value);
        Assert.True(loan!.IsReturned);
        Assert.NotNull(loan.ReturnDate);
    }

    [Fact]
    public async Task ReturnItemAsync_AfterReturn_StockBecomesAvailableAgain()
    {
        var itemId = _factory.SeedGeneralItem("Altifalante", totalQuantity: 1);
        var loanId = await _svc.LendAndGetLoanIdAsync(itemId, "101", "Staff", null);

        await _svc.ReturnItemAsync(loanId!.Value);

        // Should be lendable again
        var ok = await _svc.LendItemAsync(itemId, "202", "Staff", null);
        Assert.True(ok);
    }

    // ── GetActiveLoansByRoomAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetActiveLoansByRoomAsync_ReturnsOnlyActiveForRoom()
    {
        var itemId = _factory.SeedGeneralItem("Altifalante", totalQuantity: 2);
        var loanId = await _svc.LendAndGetLoanIdAsync(itemId, "101", "Staff", null);
        await _svc.LendItemAsync(itemId, "202", "Staff", null); // different room
        await _svc.ReturnItemAsync(loanId!.Value); // return room 101's loan

        // Room 101 has no active loans (returned); room 202 has one
        var loansFor101 = await _svc.GetActiveLoansByRoomAsync("101");
        var loansFor202 = await _svc.GetActiveLoansByRoomAsync("202");

        Assert.Empty(loansFor101);
        Assert.Single(loansFor202);
    }

    [Fact]
    public async Task GetActiveLoansByRoomAsync_IncludesItemName()
    {
        var itemId = _factory.SeedGeneralItem("Estendal", totalQuantity: 1);
        await _svc.LendItemAsync(itemId, "101", "Staff", null);

        var loans = await _svc.GetActiveLoansByRoomAsync("101");

        Assert.Single(loans);
        Assert.Equal("Estendal", loans[0].ItemName);
    }

    public void Dispose() => _factory.Dispose();
}
