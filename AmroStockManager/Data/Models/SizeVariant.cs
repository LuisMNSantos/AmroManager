using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class SizeVariant
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("product_sync_id")]
    public string ProductId { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int MinStockAlert { get; set; } = 5;
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    [JsonIgnore] public Product Product { get; set; } = null!;
    [JsonIgnore] public List<StockMovement> StockMovements { get; set; } = [];
    [JsonIgnore] public bool IsLowStock => MinStockAlert > 0 && Quantity <= MinStockAlert;
}
