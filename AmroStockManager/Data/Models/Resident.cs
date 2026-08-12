using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class Resident
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsCollaborator { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
