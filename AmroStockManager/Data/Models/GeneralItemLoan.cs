namespace AmroStockManager.Data.Models;

public class GeneralItemLoan
{
    public int Id { get; set; }
    public int GeneralItemId { get; set; }
    public GeneralItem GeneralItem { get; set; } = null!;
    public string RoomNumber { get; set; } = string.Empty;
    public string GivenBy { get; set; } = string.Empty;
    public DateTime LoanDate { get; set; } = DateTime.UtcNow;
    public DateTime? ReturnDate { get; set; }
    public string? Notes { get; set; }

    public bool IsReturned => ReturnDate.HasValue;
}
