namespace AmroStockManager.Data.Models;

public enum MovementReason { Sale, Restock, Adjustment, Trade }

public class StockMovement : ISyncable
{
    public int Id { get; set; }
    public int SizeVariantId { get; set; }
    public SizeVariant SizeVariant { get; set; } = null!;
    public int ChangeAmount { get; set; }
    public MovementReason Reason { get; set; }
    public string? RoomNumber { get; set; }
    public string? Notes { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
