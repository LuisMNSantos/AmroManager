namespace AmroStockManager.Data.Models;

public class SizeVariant : ISyncable
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Size { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int MinStockAlert { get; set; } = 5;
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public List<StockMovement> StockMovements { get; set; } = [];

    public bool IsLowStock => MinStockAlert > 0 && Quantity <= MinStockAlert;
}
