using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class DistributionRecord
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("campaign_sync_id")]
    public string CampaignId { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public DateTime? DistributedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime UpdatedAt { get; set; }

    [JsonIgnore] public bool IsDelivered => DistributedAt.HasValue;
}
