using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class RenewerKitDelivery
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("resident_sync_id")]
    public string ResidentId { get; set; } = string.Empty;

    public string ResidentName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string DeliveredBy { get; set; } = string.Empty;
    public DateTime DeliveredAt { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime UpdatedAt { get; set; }

    [JsonIgnore] public List<RenewerKitDeliveryItem> Items { get; set; } = [];
}
