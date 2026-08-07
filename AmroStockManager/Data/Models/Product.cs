namespace AmroStockManager.Data.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<SizeVariant> SizeVariants { get; set; } = [];

    public int TotalStock => SizeVariants.Sum(sv => sv.Quantity);
    public bool HasLowStock => SizeVariants.Any(sv => sv.IsLowStock);
}
