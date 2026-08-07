namespace AmroStockManager.Data.Models;

public class Resident
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsCollaborator { get; set; }
}
