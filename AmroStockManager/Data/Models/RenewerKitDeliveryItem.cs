using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class RenewerKitDeliveryItem
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("delivery_sync_id")]
    public string DeliveryId { get; set; } = string.Empty;

    [JsonPropertyName("product_sync_id")]
    public string ProductId { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("size_variant_sync_id")]
    public string SizeVariantId { get; set; } = string.Empty;

    public string Size { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime UpdatedAt { get; set; }
}
