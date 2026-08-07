namespace AmroStockManager.Data.Models;

public class GeneralItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TotalQuantity { get; set; } = 1;
    public string? Description { get; set; }
    public List<GeneralItemLoan> Loans { get; set; } = [];

    public int ActiveLoansCount => Loans.Count(l => l.ReturnDate == null);
    public int AvailableCount => TotalQuantity - ActiveLoansCount;
    public bool HasAvailable => AvailableCount > 0;
}
