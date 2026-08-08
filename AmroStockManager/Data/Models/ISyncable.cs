namespace AmroStockManager.Data.Models;

public interface ISyncable
{
    string SyncId { get; set; }
    DateTime UpdatedAt { get; set; }
}
