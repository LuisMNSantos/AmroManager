using Microsoft.EntityFrameworkCore;
using AmroStockManager.Data;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class GeneralItemService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<GeneralItem>> GetAllWithLoansAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.GeneralItems
            .Include(gi => gi.Loans)
            .OrderBy(gi => gi.Name)
            .ToListAsync();
    }

    public async Task<List<(GeneralItemLoan Loan, string ItemName)>> GetActiveLoansByRoomAsync(string roomNumber)
    {
        var room = roomNumber.Trim().ToUpper();
        await using var db = await dbFactory.CreateDbContextAsync();
        var loans = await db.GeneralItemLoans
            .Include(l => l.GeneralItem)
            .Where(l => l.RoomNumber.ToUpper() == room && l.ReturnDate == null)
            .OrderByDescending(l => l.LoanDate)
            .ToListAsync();
        return loans.Select(l => (l, l.GeneralItem?.Name ?? "?")).ToList();
    }

    public async Task<List<GeneralItemLoan>> GetHistoryAsync(int itemId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.GeneralItemLoans
            .Where(l => l.GeneralItemId == itemId)
            .OrderByDescending(l => l.LoanDate)
            .ToListAsync();
    }

    public async Task<bool> LendItemAsync(int itemId, string roomNumber, string givenBy, string? notes, int quantity = 1)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.GeneralItems.Include(gi => gi.Loans).FirstOrDefaultAsync(gi => gi.Id == itemId);
        if (item is null || item.AvailableCount < quantity) return false;

        db.GeneralItemLoans.Add(new GeneralItemLoan
        {
            GeneralItemId = itemId,
            RoomNumber = roomNumber.Trim(),
            GivenBy = givenBy.Trim(),
            Quantity = quantity,
            Notes = notes,
            LoanDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return true;
    }

    // Returns the created loan ID so callers (e.g. ReservationService) can link it.
    public async Task<int?> LendAndGetLoanIdAsync(int itemId, string roomNumber, string givenBy, string? notes, int quantity = 1)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.GeneralItems.Include(gi => gi.Loans).FirstOrDefaultAsync(gi => gi.Id == itemId);
        if (item is null || item.AvailableCount < quantity) return null;

        var loan = new GeneralItemLoan
        {
            GeneralItemId = itemId,
            RoomNumber = roomNumber.Trim(),
            GivenBy = givenBy.Trim(),
            Quantity = quantity,
            Notes = notes,
            LoanDate = DateTime.UtcNow
        };
        db.GeneralItemLoans.Add(loan);
        await db.SaveChangesAsync();
        return loan.Id;
    }

    public async Task ReturnItemAsync(int loanId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var loan = await db.GeneralItemLoans.FindAsync(loanId);
        if (loan is null || loan.IsReturned) return;

        loan.ReturnDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task DeleteItemAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.GeneralItems.FindAsync(id);
        if (item is not null)
        {
            db.GeneralItems.Remove(item);
            await db.SaveChangesAsync();
        }
    }

    public async Task<GeneralItem> AddOrUpdateItemAsync(GeneralItem item)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (item.Id == 0)
            db.GeneralItems.Add(item);
        else
        {
            var existing = await db.GeneralItems.FindAsync(item.Id);
            if (existing is null) return item;
            existing.Name = item.Name;
            existing.TotalQuantity = item.TotalQuantity;
            existing.Description = item.Description;
        }
        await db.SaveChangesAsync();
        return item;
    }
}
