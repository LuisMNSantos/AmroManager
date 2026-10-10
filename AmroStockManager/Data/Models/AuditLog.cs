using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class AuditLog
{
    [JsonPropertyName("sync_id")]      public string    Id          { get; set; } = string.Empty;
    [JsonPropertyName("action")]       public string    Action      { get; set; } = string.Empty;
    [JsonPropertyName("details")]      public string?   Details     { get; set; }
    [JsonPropertyName("performed_at")] public DateTime  PerformedAt { get; set; }
}
