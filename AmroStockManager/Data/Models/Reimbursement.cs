using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class Reimbursement
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("resident_sync_id")]
    public string? ResidentId { get; set; }

    [JsonPropertyName("resident_name")]
    public string ResidentName { get; set; } = string.Empty;

    [JsonPropertyName("room_number")]
    public string RoomNumber { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string? Reason { get; set; }

    [JsonPropertyName("is_paid")]
    public bool IsPaid { get; set; }

    [JsonPropertyName("is_deleted")]
    public bool IsDeleted { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
