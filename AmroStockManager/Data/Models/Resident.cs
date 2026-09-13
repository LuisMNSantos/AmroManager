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
    [JsonPropertyName("collaborator_role")]
    public string? CollaboratorRole { get; set; }
    [JsonPropertyName("is_renewer")]
    public bool IsRenewer { get; set; }
    [JsonPropertyName("free_overnights")]
    public int FreeOvernights { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
