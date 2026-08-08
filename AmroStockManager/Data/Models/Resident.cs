namespace AmroStockManager.Data.Models;

public class Resident : ISyncable
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsCollaborator { get; set; }
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
