using System.Text;
using Microsoft.EntityFrameworkCore;
using AmroStockManager.Data;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public record CleanupPreview(
    int ReturnedLoans,
    int OldReservations,
    int OldMovements,
    int OldDeliveries);

public class MaintenanceService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<CleanupPreview> GetPreviewAsync(int monthsOlderThan)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-monthsOlderThan);
        await using var db = await dbFactory.CreateDbContextAsync();
        return new CleanupPreview(
            ReturnedLoans:   await db.GeneralItemLoans.CountAsync(l => l.ReturnDate != null && l.ReturnDate < cutoff),
            OldReservations: await db.Reservations.CountAsync(r => r.EndTime < cutoff),
            OldMovements:    await db.StockMovements.CountAsync(m => m.Date < cutoff),
            OldDeliveries:   await db.Deliveries.CountAsync(d => d.CollectedAt != null && d.CollectedAt < cutoff)
        );
    }

    public async Task<string> ExportToCsvAsync(int monthsOlderThan)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-monthsOlderThan);
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"AmroStock_Export_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(dir);

        await using var db = await dbFactory.CreateDbContextAsync();

        // Returned loans
        var loans = await db.GeneralItemLoans
            .Include(l => l.GeneralItem)
            .Where(l => l.ReturnDate != null && l.ReturnDate < cutoff)
            .OrderByDescending(l => l.ReturnDate)
            .ToListAsync();
        if (loans.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Data Empréstimo,Artigo,Quarto,Entregue Por,Quantidade,Notas,Data Devolução");
            foreach (var l in loans)
                sb.AppendLine(string.Join(",", [
                    Csv(l.LoanDate.ToLocalTime().ToString("dd/MM/yyyy HH:mm")),
                    Csv(l.GeneralItem.Name),
                    Csv(l.RoomNumber),
                    Csv(l.GivenBy),
                    l.Quantity.ToString(),
                    Csv(l.Notes),
                    Csv(l.ReturnDate!.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))
                ]));
            await File.WriteAllTextAsync(Path.Combine(dir, "historico_emprestimos.csv"), sb.ToString(), Encoding.UTF8);
        }

        // Old reservations
        var reservations = await db.Reservations
            .Where(r => r.EndTime < cutoff)
            .OrderByDescending(r => r.StartTime)
            .ToListAsync();
        if (reservations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Espaço,Quarto,Reservado Por,Início,Fim,Notas,Estado");
            foreach (var r in reservations)
                sb.AppendLine(string.Join(",", [
                    Csv(r.Space == ReservationSpace.Cozinha ? "Cozinha MasterChef" : "Cinema"),
                    Csv(r.RoomNumber),
                    Csv(r.ReservedBy),
                    Csv(r.StartTime.ToLocalTime().ToString("dd/MM/yyyy HH:mm")),
                    Csv(r.EndTime.ToLocalTime().ToString("dd/MM/yyyy HH:mm")),
                    Csv(r.Notes),
                    r.IsCancelled ? "Cancelada" : "Concluída"
                ]));
            await File.WriteAllTextAsync(Path.Combine(dir, "historico_reservas.csv"), sb.ToString(), Encoding.UTF8);
        }

        // Old stock movements
        var movements = await db.StockMovements
            .Include(m => m.SizeVariant).ThenInclude(sv => sv.Product)
            .Where(m => m.Date < cutoff)
            .OrderByDescending(m => m.Date)
            .ToListAsync();
        if (movements.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Data,Produto,Tamanho,Quarto,Movimento,Motivo,Notas");
            foreach (var m in movements)
                sb.AppendLine(string.Join(",", [
                    Csv(m.Date.ToLocalTime().ToString("dd/MM/yyyy HH:mm")),
                    Csv(m.SizeVariant.Product.Name),
                    Csv(m.SizeVariant.Size),
                    Csv(m.RoomNumber),
                    m.ChangeAmount.ToString(),
                    Csv(m.Reason.ToString()),
                    Csv(m.Notes)
                ]));
            await File.WriteAllTextAsync(Path.Combine(dir, "historico_stock.csv"), sb.ToString(), Encoding.UTF8);
        }

        // Old collected deliveries
        var deliveries = await db.Deliveries
            .Where(d => d.CollectedAt != null && d.CollectedAt < cutoff)
            .OrderByDescending(d => d.ArrivedAt)
            .ToListAsync();
        if (deliveries.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Tipo,Quarto,Quantidade,Data Chegada,Data Entrega,Notas");
            foreach (var d in deliveries)
                sb.AppendLine(string.Join(",", [
                    d.Type == DeliveryType.Encomenda ? "Encomenda" : "Carta",
                    Csv(d.RoomNumber),
                    d.Quantity.ToString(),
                    Csv(d.ArrivedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm")),
                    Csv(d.CollectedAt!.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm")),
                    Csv(d.Notes)
                ]));
            await File.WriteAllTextAsync(Path.Combine(dir, "historico_encomendas.csv"), sb.ToString(), Encoding.UTF8);
        }

        return dir;
    }

    public async Task CleanupAsync(int monthsOlderThan)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-monthsOlderThan);
        await using var db = await dbFactory.CreateDbContextAsync();

        // 1. Delete old reservations (no cascade impact on loans)
        var oldReservations = await db.Reservations.Where(r => r.EndTime < cutoff).ToListAsync();
        db.Reservations.RemoveRange(oldReservations);
        await db.SaveChangesAsync();

        // 2. Delete old returned loans (SQLite SET NULL handles remaining reservation refs)
        var oldLoans = await db.GeneralItemLoans.Where(l => l.ReturnDate != null && l.ReturnDate < cutoff).ToListAsync();
        db.GeneralItemLoans.RemoveRange(oldLoans);
        await db.SaveChangesAsync();

        // 3. Delete old stock movements (no FK references)
        await db.StockMovements.Where(m => m.Date < cutoff).ExecuteDeleteAsync();

        // 4. Delete old collected deliveries (no FK references)
        await db.Deliveries.Where(d => d.CollectedAt != null && d.CollectedAt < cutoff).ExecuteDeleteAsync();

        // 5. Compact the database file
        await db.Database.ExecuteSqlRawAsync("VACUUM");
    }

    private static string Csv(object? value) =>
        $"\"{(value?.ToString() ?? "").Replace("\"", "\"\"")}\"";
}
