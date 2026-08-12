using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public enum DeliveryType
{
    Encomenda = 0,
    Carta     = 1
}

public class Delivery
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;
    public DeliveryType Type { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public DateTime ArrivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CollectedAt { get; set; }
    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsDelivered { get; set; }

    [JsonIgnore] public bool IsCollected => IsDelivered;
}
