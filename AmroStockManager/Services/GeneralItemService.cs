using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class GeneralItemService(SupabaseClient db, CacheService cache)
{
    private static readonly TimeSpan _itemsTtl = TimeSpan.FromSeconds(30);
    private const string _itemsKey = "general_items:all";

    public Task<List<GeneralItem>> GetAllWithLoansAsync() =>
        cache.GetOrFetchAsync(_itemsKey, FetchAllWithLoansAsync, _itemsTtl);

    private async Task<List<GeneralItem>> FetchAllWithLoansAsync()
    {
        var itemsTask = db.GetAsync<GeneralItem>("general_items", "is_deleted=eq.false&order=name.asc");
        var loansTask = db.GetAsync<GeneralItemLoan>("general_item_loans", "is_deleted=eq.false");
        await Task.WhenAll(itemsTask, loansTask);

        var byItem = loansTask.Result.GroupBy(l => l.GeneralItemId)
                                     .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var item in itemsTask.Result)
            item.Loans = byItem.TryGetValue(item.Id, out var ls) ? ls : [];
        return itemsTask.Result;
    }

    public async Task<List<(GeneralItemLoan Loan, string ItemName)>> GetActiveLoansByRoomAsync(string roomNumber)
    {
        var room = Uri.EscapeDataString(roomNumber.Trim().ToUpper());
        var loans = await db.GetAsync<GeneralItemLoan>("general_item_loans",
            $"is_deleted=eq.false&is_returned=eq.false&room_number=eq.{room}&order=loan_date.desc");
        if (loans.Count == 0) return [];

        var ids = string.Join(",", loans.Select(l => l.GeneralItemId).Distinct());
        var items = await db.GetAsync<GeneralItem>("general_items", $"sync_id=in.({ids})&select=sync_id,name");
        var nameById = items.ToDictionary(i => i.Id, i => i.Name);
        return loans.Select(l => (l, nameById.TryGetValue(l.GeneralItemId, out var n) ? n : "?")).ToList();
    }

    public async Task<List<GeneralItemLoan>> GetHistoryAsync(string itemId)
    {
        return await db.GetAsync<GeneralItemLoan>("general_item_loans",
            $"general_item_sync_id=eq.{itemId}&order=loan_date.desc");
    }

    public async Task<bool> LendItemAsync(string itemId, string roomNumber, string givenBy, string? notes, int quantity = 1)
    {
        var item = await FetchItemWithLoansAsync(itemId);
        if (item is null || item.AvailableCount < quantity) return false;

        await db.InsertAsync<GeneralItemLoan>("general_item_loans", new
        {
            sync_id              = Guid.NewGuid().ToString(),
            general_item_sync_id = itemId,
            room_number          = roomNumber.Trim(),
            given_by             = givenBy.Trim(),
            quantity             = quantity,
            notes                = notes,
            loan_date            = DateTime.UtcNow,
            is_returned          = false,
            is_deleted           = false,
            updated_at           = DateTime.UtcNow
        });
        cache.Invalidate(_itemsKey);
        return true;
    }

    public async Task<string?> LendAndGetLoanIdAsync(string itemId, string roomNumber, string givenBy, string? notes, int quantity = 1)
    {
        var item = await FetchItemWithLoansAsync(itemId);
        if (item is null || item.AvailableCount < quantity) return null;

        var loan = await db.InsertAsync<GeneralItemLoan>("general_item_loans", new
        {
            sync_id              = Guid.NewGuid().ToString(),
            general_item_sync_id = itemId,
            room_number          = roomNumber.Trim(),
            given_by             = givenBy.Trim(),
            quantity             = quantity,
            notes                = notes,
            loan_date            = DateTime.UtcNow,
            is_returned          = false,
            is_deleted           = false,
            updated_at           = DateTime.UtcNow
        });
        cache.Invalidate(_itemsKey);
        return loan?.Id;
    }

    public async Task ReturnItemAsync(string loanId)
    {
        var loans = await db.GetAsync<GeneralItemLoan>("general_item_loans", $"sync_id=eq.{loanId}&select=sync_id,is_returned");
        if (loans is not [var loan] || loan.IsReturned) return;

        await db.PatchAsync("general_item_loans", $"sync_id=eq.{loanId}", new
        {
            return_date = DateTime.UtcNow,
            is_returned = true,
            updated_at  = DateTime.UtcNow
        });
        cache.Invalidate(_itemsKey);
    }

    public async Task DeleteItemAsync(string id)
    {
        await db.PatchAsync("general_items",      $"sync_id=eq.{id}",             new { is_deleted = true, updated_at = DateTime.UtcNow });
        await db.PatchAsync("general_item_loans", $"general_item_sync_id=eq.{id}", new { is_deleted = true, updated_at = DateTime.UtcNow });
        cache.Invalidate(_itemsKey);
    }

    public async Task<GeneralItem> AddOrUpdateItemAsync(GeneralItem item)
    {
        if (string.IsNullOrEmpty(item.Id))
        {
            var created = await db.InsertAsync<GeneralItem>("general_items", new
            {
                sync_id             = Guid.NewGuid().ToString(),
                name                = item.Name,
                total_quantity      = item.TotalQuantity,
                description         = item.Description,
                linked_item_sync_id = item.LinkedItemId,
                updated_at          = DateTime.UtcNow,
                is_deleted          = false
            });
            item.Id = created!.Id;
        }
        else
        {
            await db.PatchAsync("general_items", $"sync_id=eq.{item.Id}", new
            {
                name           = item.Name,
                total_quantity = item.TotalQuantity,
                description    = item.Description,
                updated_at     = DateTime.UtcNow
            });
        }
        cache.Invalidate(_itemsKey);
        return item;
    }

    private async Task<GeneralItem?> FetchItemWithLoansAsync(string itemId)
    {
        var itemsTask = db.GetAsync<GeneralItem>("general_items", $"sync_id=eq.{itemId}&is_deleted=eq.false");
        var loansTask = db.GetAsync<GeneralItemLoan>("general_item_loans", $"general_item_sync_id=eq.{itemId}&is_deleted=eq.false");
        await Task.WhenAll(itemsTask, loansTask);

        var item = itemsTask.Result.FirstOrDefault();
        if (item is not null) item.Loans = loansTask.Result;
        return item;
    }
}
