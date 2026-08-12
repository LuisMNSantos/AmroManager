using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class GeneralItem
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TotalQuantity { get; set; } = 1;
    public string? Description { get; set; }
    [JsonPropertyName("linked_item_sync_id")]
    public string? LinkedItemId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public List<GeneralItemLoan> Loans { get; set; } = [];

    [JsonIgnore] public int ActiveLoansCount => Loans.Where(l => !l.IsReturned && !l.IsDeleted).Sum(l => l.Quantity);
    [JsonIgnore] public int AvailableCount => TotalQuantity - ActiveLoansCount;
    [JsonIgnore] public bool HasAvailable => AvailableCount > 0;
}
