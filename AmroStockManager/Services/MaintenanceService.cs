using System.Text;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public record CleanupPreview(
    int ReturnedLoans,
    int OldReservations,
    int OldMovements,
    int OldDeliveries);

public class MaintenanceService(SupabaseClient db)
{
    public async Task<CleanupPreview> GetPreviewAsync(int monthsOlderThan)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-monthsOlderThan).ToString("O");
        var t1 = db.GetCountAsync("general_item_loans", $"is_deleted=eq.false&is_returned=eq.true&return_date=lt.{cutoff}");
        var t2 = db.GetCountAsync("reservations",       $"is_deleted=eq.false&end_time=lt.{cutoff}");
        var t3 = db.GetCountAsync("stock_movements",    $"is_deleted=eq.false&date=lt.{cutoff}");
        var t4 = db.GetCountAsync("deliveries",         $"is_deleted=eq.false&is_delivered=eq.true&collected_at=lt.{cutoff}");
        await Task.WhenAll(t1, t2, t3, t4);
        return new CleanupPreview(t1.Result, t2.Result, t3.Result, t4.Result);
    }

    public async Task<string> ExportToCsvAsync(int monthsOlderThan)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-monthsOlderThan);
        var cutoffStr = cutoff.ToString("O");
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"AmroStock_Export_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(dir);

        var loans = await db.GetAsync<GeneralItemLoan>("general_item_loans",
            $"is_deleted=eq.false&is_returned=eq.true&return_date=lt.{cutoffStr}&order=return_date.desc");
        if (loans.Count > 0)
        {
            var itemIds   = loans.Select(l => l.GeneralItemId).Distinct();
            var items     = await db.GetAsync<GeneralItem>("general_items", $"sync_id=in.({string.Join(",", itemIds)})&select=sync_id,name");
            var itemById  = items.ToDictionary(i => i.Id);
            foreach (var l in loans) l.GeneralItem = itemById.GetValueOrDefault(l.GeneralItemId)!;
            var sb = new StringBuilder();
            sb.AppendLine("Data Empréstimo,Artigo,Quarto,Entregue Por,Quantidade,Notas,Data Devolução");
            foreach (var l in loans)
                sb.AppendLine(string.Join(",", [
                    Csv(l.LoanDate.ToLocalTime().ToString("dd/MM/yyyy HH:mm")),
                    Csv(l.GeneralItem?.Name),
                    Csv(l.RoomNumber),
                    Csv(l.GivenBy),
                    l.Quantity.ToString(),
                    Csv(l.Notes),
                    Csv(l.ReturnDate?.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))
                ]));
            await File.WriteAllTextAsync(Path.Combine(dir, "historico_emprestimos.csv"), sb.ToString(), Encoding.UTF8);
        }

        var reservations = await db.GetAsync<Reservation>("reservations",
            $"is_deleted=eq.false&end_time=lt.{cutoffStr}&order=start_time.desc");
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

        var movements = await db.GetAsync<StockMovement>("stock_movements",
            $"is_deleted=eq.false&date=lt.{cutoffStr}&order=date.desc");
        if (movements.Count > 0)
        {
            var variantIds = movements.Select(m => m.SizeVariantId).Distinct();
            var variants   = await db.GetAsync<SizeVariant>("size_variants", $"sync_id=in.({string.Join(",", variantIds)})");
            var productIds = variants.Select(v => v.ProductId).Distinct();
            var products   = await db.GetAsync<Product>("products", $"sync_id=in.({string.Join(",", productIds)})");
            var productById = products.ToDictionary(p => p.Id);
            var variantById = variants.ToDictionary(v => v.Id);
            foreach (var v in variants)
                v.Product = productById.GetValueOrDefault(v.ProductId) ?? new Product();
            foreach (var m in movements)
                m.SizeVariant = variantById.GetValueOrDefault(m.SizeVariantId) ?? new SizeVariant();
            var sb = new StringBuilder();
            sb.AppendLine("Data,Produto,Tamanho,Quarto,Movimento,Motivo,Notas");
            foreach (var m in movements)
                sb.AppendLine(string.Join(",", [
                    Csv(m.Date.ToLocalTime().ToString("dd/MM/yyyy HH:mm")),
                    Csv(m.SizeVariant?.Product?.Name),
                    Csv(m.SizeVariant?.Size),
                    Csv(m.RoomNumber),
                    m.ChangeAmount.ToString(),
                    Csv(m.Reason.ToString()),
                    Csv(m.Notes)
                ]));
            await File.WriteAllTextAsync(Path.Combine(dir, "historico_stock.csv"), sb.ToString(), Encoding.UTF8);
        }

        var deliveries = await db.GetAsync<Delivery>("deliveries",
            $"is_deleted=eq.false&is_delivered=eq.true&collected_at=lt.{cutoffStr}&order=arrived_at.desc");
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
                    Csv(d.CollectedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm")),
                    Csv(d.Notes)
                ]));
            await File.WriteAllTextAsync(Path.Combine(dir, "historico_encomendas.csv"), sb.ToString(), Encoding.UTF8);
        }

        return dir;
    }

    public async Task CleanupAsync(int monthsOlderThan)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-monthsOlderThan).ToString("O");
        var patch  = new { is_deleted = true, updated_at = DateTime.UtcNow };

        await db.PatchAsync("general_item_loans", $"is_deleted=eq.false&is_returned=eq.true&return_date=lt.{cutoff}", patch);
        await db.PatchAsync("reservations",       $"is_deleted=eq.false&end_time=lt.{cutoff}",                        patch);
        await db.PatchAsync("stock_movements",    $"is_deleted=eq.false&date=lt.{cutoff}",                            patch);
        await db.PatchAsync("deliveries",         $"is_deleted=eq.false&is_delivered=eq.true&collected_at=lt.{cutoff}", patch);
    }

    public Task<int> GetPiiPurgePreviewAsync(int monthsOlderThan)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-monthsOlderThan).ToString("O");
        return db.GetCountAsync("residents", $"is_deleted=eq.true&updated_at=lt.{cutoff}");
    }

    public async Task PurgePiiAsync(int monthsOlderThan)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-monthsOlderThan).ToString("O");
        await db.DeleteAsync("residents", $"is_deleted=eq.true&updated_at=lt.{cutoff}");
    }

    private static string Csv(object? value) =>
        $"\"{(value?.ToString() ?? "").Replace("\"", "\"\"")}\"";
}
