namespace AmroStockManager.Data.Models;

public class GeneralItem : ISyncable
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TotalQuantity { get; set; } = 1;
    public string? Description { get; set; }
    public int? LinkedItemId { get; set; }
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public List<GeneralItemLoan> Loans { get; set; } = [];

    public int ActiveLoansCount => Loans.Where(l => l.ReturnDate == null).Sum(l => l.Quantity);
    public int AvailableCount => TotalQuantity - ActiveLoansCount;
    public bool HasAvailable => AvailableCount > 0;
}
