namespace AmroStockManager.Data.Models;

public class AuditLog
{
    public string Id { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime PerformedAt { get; set; }
}
