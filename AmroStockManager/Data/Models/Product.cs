using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class Product
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public List<SizeVariant> SizeVariants { get; set; } = [];

    [JsonIgnore] public int TotalStock => SizeVariants.Sum(sv => sv.Quantity);
    [JsonIgnore] public bool HasLowStock => SizeVariants.Any(sv => sv.IsLowStock);
}
