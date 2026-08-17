using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class DistributionCampaign
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("size_variant_sync_id")]
    public string SizeVariantId { get; set; } = string.Empty;
    public string VariantLabel { get; set; } = string.Empty;
    public int QuantityPerRoom { get; set; } = 1;
    public int TotalRooms { get; set; }
    public int DeliveredCount { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTime UpdatedAt { get; set; }

    [JsonIgnore] public List<DistributionRecord> Records { get; set; } = [];
    [JsonIgnore] public int PendingCount => TotalRooms - DeliveredCount;
    [JsonIgnore] public double ProgressPct => TotalRooms == 0 ? 0 : (double)DeliveredCount / TotalRooms * 100;
}
