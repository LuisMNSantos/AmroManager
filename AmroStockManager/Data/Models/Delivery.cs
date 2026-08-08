namespace AmroStockManager.Data.Models;

public enum DeliveryType
{
    Encomenda = 0,
    Carta     = 1
}

public class Delivery : ISyncable
{
    public int Id { get; set; }
    public DeliveryType Type { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public DateTime ArrivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CollectedAt { get; set; }
    public string? Notes { get; set; }
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    public bool IsCollected => CollectedAt.HasValue;
}
