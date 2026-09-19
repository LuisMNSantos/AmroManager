using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class Room
{
    public string  Number      { get; set; } = string.Empty;
    public string  Code        { get; set; } = string.Empty;
    public string  Designation { get; set; } = string.Empty;

    [JsonPropertyName("visit_pin")]
    public string? VisitPin    { get; set; }
}
