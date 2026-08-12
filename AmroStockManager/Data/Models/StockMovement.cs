using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public enum MovementReason { Sale, Restock, Adjustment, Trade }

public class StockMovement
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("size_variant_sync_id")]
    public string SizeVariantId { get; set; } = string.Empty;
    public int ChangeAmount { get; set; }
    public MovementReason Reason { get; set; }
    public string? RoomNumber { get; set; }
    public string? Notes { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    [JsonIgnore] public SizeVariant SizeVariant { get; set; } = null!;
}
