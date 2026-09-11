using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class PendingRegistration
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Notes { get; set; }
    public DateTime RequestedAt { get; set; }
}
