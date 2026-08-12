using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class GeneralItemLoan
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("general_item_sync_id")]
    public string GeneralItemId { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string GivenBy { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public DateTime LoanDate { get; set; } = DateTime.UtcNow;
    public DateTime? ReturnDate { get; set; }
    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsReturned { get; set; }

    [JsonIgnore] public GeneralItem GeneralItem { get; set; } = null!;
}
