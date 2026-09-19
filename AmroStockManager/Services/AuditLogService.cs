using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class AuditLogService(ISupabaseClient db)
{
    public Task LogAsync(string action, string? details = null) =>
        db.InsertAsync<AuditLog>("audit_logs", new { action, details });

    public Task<List<AuditLog>> GetAllAsync() =>
        db.GetAsync<AuditLog>("audit_logs", "order=performed_at.desc&limit=2000");
}
