using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class RenewerKitItem
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("product_sync_id")]
    public string ProductId { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }
    public DateTime UpdatedAt { get; set; }

    [JsonIgnore] public Product? Product { get; set; }
}
