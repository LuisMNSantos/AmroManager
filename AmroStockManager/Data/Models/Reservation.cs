namespace AmroStockManager.Data.Models;

public enum ReservationSpace
{
    Cozinha = 0,
    Cinema  = 1
}

public class Reservation : ISyncable
{
    public int Id { get; set; }
    public ReservationSpace Space { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public string ReservedBy { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsCancelled { get; set; }
    public int? AccessCardLoanId { get; set; }
    public GeneralItemLoan? AccessCardLoan { get; set; }
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
