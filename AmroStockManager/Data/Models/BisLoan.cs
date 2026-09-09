using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class BisLoan
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string GivenBy { get; set; } = string.Empty;
    public DateTime LoanDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public bool IsReturned { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime UpdatedAt { get; set; }

    [JsonIgnore]
    public int DaysOut => (int)(DateTime.UtcNow - LoanDate).TotalDays;
}
