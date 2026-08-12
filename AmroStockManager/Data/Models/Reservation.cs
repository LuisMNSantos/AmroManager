using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public enum ReservationSpace
{
    Cozinha = 0,
    Cinema  = 1
}

public class Reservation
{
    [JsonPropertyName("sync_id")]
    public string Id { get; set; } = string.Empty;
    public ReservationSpace Space { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public string ReservedBy { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsCancelled { get; set; }
    public bool IsActivated { get; set; }
    public bool IsCompleted { get; set; }
    [JsonPropertyName("access_card_loan_sync_id")]
    public string? AccessCardLoanId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    [JsonIgnore] public GeneralItemLoan? AccessCardLoan { get; set; }
}
