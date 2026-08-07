namespace AmroStockManager.Data.Models;

public class SizeVariant
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Size { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int MinStockAlert { get; set; } = 5;
    public List<StockMovement> StockMovements { get; set; } = [];

    public bool IsLowStock => MinStockAlert > 0 && Quantity <= MinStockAlert;
}
