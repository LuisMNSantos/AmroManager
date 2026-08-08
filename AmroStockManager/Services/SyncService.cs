using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using AmroStockManager.Data;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class SyncService(IDbContextFactory<AppDbContext> dbFactory)
{
    private const string LastKey = "sync_last";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling       = JsonNumberHandling.AllowReadingFromString
    };

    // ── Config ────────────────────────────────────────────────────────────────

    private static string Url => AppSecrets.SupabaseUrl.Trim();
    private static string Key => AppSecrets.SupabaseKey.Trim();

    public DateTime? LastSync
    {
        get
        {
            var raw = Preferences.Default.Get(LastKey, string.Empty);
            return string.IsNullOrEmpty(raw)
                ? null
                : DateTime.Parse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind);
        }
        private set => Preferences.Default.Set(LastKey, value?.ToString("O") ?? string.Empty);
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Key);

    // ── Main sync entry point ─────────────────────────────────────────────────

    public async Task<(bool Ok, string Message)> SyncAsync()
    {
        if (!IsConfigured)
            return (false, "Configuração de sincronização em falta.");

        try
        {
            using var http = CreateClient();

            // Verify connectivity before touching the DB
            var ping = await http.GetAsync("rest/v1/");
            if (!ping.IsSuccessStatusCode)
                return (false, $"Não foi possível ligar ({Url}). HTTP {(int)ping.StatusCode}.");

            await using var db = await dbFactory.CreateDbContextAsync();

            // ── Upload (local → Supabase) ────────────────────────────────────
            await UpsertRows(http, "products",
                (await db.Products.ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId))
                    .Select(x => new PrdRow(x.SyncId, x.Name, x.Type, x.Color, x.SKU, x.CreatedAt, x.UpdatedAt)));

            await UpsertRows(http, "size_variants",
                (await db.SizeVariants.Include(sv => sv.Product).ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId) && !string.IsNullOrEmpty(x.Product?.SyncId))
                    .Select(x => new SvRow(x.SyncId, x.Product!.SyncId, x.Size, x.Quantity, x.MinStockAlert, x.UpdatedAt)));

            await UpsertRows(http, "stock_movements",
                (await db.StockMovements.Include(sm => sm.SizeVariant).ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId) && !string.IsNullOrEmpty(x.SizeVariant?.SyncId))
                    .Select(x => new SmRow(x.SyncId, x.SizeVariant!.SyncId, x.ChangeAmount, (int)x.Reason,
                                           x.RoomNumber, x.Notes, x.Date, x.UpdatedAt)));

            await UpsertRows(http, "general_items",
                (await db.GeneralItems.ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId))
                    .Select(x => new GiRow(x.SyncId, x.Name, x.TotalQuantity, x.Description, x.UpdatedAt)));

            await UpsertRows(http, "general_item_loans",
                (await db.GeneralItemLoans.ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId))
                    .Select(x => new GilRow(x.SyncId, x.GeneralItemSyncId, x.RoomNumber, x.GivenBy,
                                            x.Quantity, x.LoanDate, x.ReturnDate, x.Notes, x.UpdatedAt)));

            await UpsertRows(http, "residents",
                (await db.Residents.ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId))
                    .Select(x => new RsdRow(x.SyncId, x.Name, x.RoomNumber, x.PhoneNumber, x.IsCollaborator, x.UpdatedAt)));

            await UpsertRows(http, "reservations",
                (await db.Reservations.ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId))
                    .Select(x => new ResRow(x.SyncId, (int)x.Space, x.RoomNumber, x.ReservedBy,
                                            x.StartTime, x.EndTime, x.Notes, x.CreatedAt, x.IsCancelled, x.UpdatedAt)));

            await UpsertRows(http, "deliveries",
                (await db.Deliveries.ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId))
                    .Select(x => new DelRow(x.SyncId, (int)x.Type, x.RoomNumber, x.Quantity,
                                            x.ArrivedAt, x.CollectedAt, x.Notes, x.UpdatedAt)));

            // ── Download (Supabase → local) ──────────────────────────────────
            var remotePrds   = await FetchRows<PrdRow>(http, "products");
            var remoteSvs    = await FetchRows<SvRow> (http, "size_variants");
            var remoteSms    = await FetchRows<SmRow> (http, "stock_movements");
            var remoteItems  = await FetchRows<GiRow> (http, "general_items");
            var remoteLoans  = await FetchRows<GilRow>(http, "general_item_loans");
            var remoteRsds   = await FetchRows<RsdRow>(http, "residents");
            var remoteRes    = await FetchRows<ResRow> (http, "reservations");
            var remoteDels   = await FetchRows<DelRow> (http, "deliveries");

            // Merge order respects FK dependencies
            db.IsSyncing = true;
            await MergeProducts        (db, remotePrds);
            await MergeSizeVariants    (db, remoteSvs);
            await MergeStockMovements  (db, remoteSms);
            await MergeGeneralItems    (db, remoteItems);
            await MergeGeneralItemLoans(db, remoteLoans);
            await MergeResidents       (db, remoteRsds);
            await MergeReservations    (db, remoteRes);
            await MergeDeliveries      (db, remoteDels);
            await db.SaveChangesAsync();

            LastSync = DateTime.UtcNow;
            return (true, $"Sincronizado às {LastSync.Value.ToLocalTime():HH:mm}.");
        }
        catch (Exception ex)
        {
            return (false, $"Erro: {ex.Message}");
        }
    }

    // ── HTTP helpers ──────────────────────────────────────────────────────────

    private HttpClient CreateClient()
    {
        var http = new HttpClient { BaseAddress = new Uri(Url.TrimEnd('/') + "/") };
        http.DefaultRequestHeaders.Add("apikey", Key);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        return http;
    }

    private static async Task UpsertRows<T>(HttpClient http, string table, IEnumerable<T> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0) return;

        var req = new HttpRequestMessage(HttpMethod.Post, $"rest/v1/{table}?on_conflict=sync_id")
        {
            Content = JsonContent.Create(list, options: JsonOpts)
        };
        req.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
        var resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Erro {(int)resp.StatusCode} em '{table}': {body}");
        }
    }

    private static async Task<List<T>> FetchRows<T>(HttpClient http, string table)
    {
        var resp = await http.GetAsync($"rest/v1/{table}?select=*");
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Tabela '{table}' não encontrada no Supabase (HTTP {(int)resp.StatusCode}). Verifica se as tabelas foram criadas.");
        return await resp.Content.ReadFromJsonAsync<List<T>>(JsonOpts) ?? [];
    }

    // ── Merge helpers ─────────────────────────────────────────────────────────

    private static async Task MergeGeneralItems(AppDbContext db, List<GiRow> remote)
    {
        var local = await db.GeneralItems.ToDictionaryAsync(x => x.SyncId);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.TryGetValue(r.SyncId!, out var l))
            {
                if (r.UpdatedAt <= l.UpdatedAt) continue;
                l.Name = r.Name; l.TotalQuantity = r.TotalQuantity;
                l.Description = r.Description; l.UpdatedAt = r.UpdatedAt;
            }
            else
            {
                db.GeneralItems.Add(new GeneralItem
                {
                    SyncId = r.SyncId!, Name = r.Name,
                    TotalQuantity = r.TotalQuantity, Description = r.Description,
                    UpdatedAt = r.UpdatedAt
                });
            }
        }
    }

    private static async Task MergeGeneralItemLoans(AppDbContext db, List<GilRow> remote)
    {
        var local   = await db.GeneralItemLoans.ToDictionaryAsync(x => x.SyncId);
        var itemMap = await db.GeneralItems.ToDictionaryAsync(x => x.SyncId, x => x.Id);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.TryGetValue(r.SyncId!, out var l))
            {
                if (r.UpdatedAt <= l.UpdatedAt) continue;
                l.RoomNumber = r.RoomNumber; l.GivenBy = r.GivenBy;
                l.Quantity = r.Quantity; l.LoanDate = r.LoanDate;
                l.ReturnDate = r.ReturnDate; l.Notes = r.Notes;
                l.UpdatedAt = r.UpdatedAt;
            }
            else
            {
                if (string.IsNullOrEmpty(r.GeneralItemSyncId) ||
                    !itemMap.TryGetValue(r.GeneralItemSyncId, out var localItemId)) continue;
                db.GeneralItemLoans.Add(new GeneralItemLoan
                {
                    SyncId = r.SyncId!, GeneralItemId = localItemId,
                    GeneralItemSyncId = r.GeneralItemSyncId, RoomNumber = r.RoomNumber,
                    GivenBy = r.GivenBy, Quantity = r.Quantity, LoanDate = r.LoanDate,
                    ReturnDate = r.ReturnDate, Notes = r.Notes, UpdatedAt = r.UpdatedAt
                });
            }
        }
    }

    private static async Task MergeResidents(AppDbContext db, List<RsdRow> remote)
    {
        var local = await db.Residents.ToDictionaryAsync(x => x.SyncId);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.TryGetValue(r.SyncId!, out var l))
            {
                if (r.UpdatedAt <= l.UpdatedAt) continue;
                l.Name = r.Name; l.RoomNumber = r.RoomNumber;
                l.PhoneNumber = r.PhoneNumber; l.IsCollaborator = r.IsCollaborator;
                l.UpdatedAt = r.UpdatedAt;
            }
            else
            {
                db.Residents.Add(new Resident
                {
                    SyncId = r.SyncId!, Name = r.Name, RoomNumber = r.RoomNumber,
                    PhoneNumber = r.PhoneNumber, IsCollaborator = r.IsCollaborator,
                    UpdatedAt = r.UpdatedAt
                });
            }
        }
    }

    private static async Task MergeReservations(AppDbContext db, List<ResRow> remote)
    {
        var local = await db.Reservations.ToDictionaryAsync(x => x.SyncId);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.TryGetValue(r.SyncId!, out var l))
            {
                if (r.UpdatedAt <= l.UpdatedAt) continue;
                l.Space = (ReservationSpace)r.Space; l.RoomNumber = r.RoomNumber;
                l.ReservedBy = r.ReservedBy; l.StartTime = r.StartTime;
                l.EndTime = r.EndTime; l.Notes = r.Notes;
                l.IsCancelled = r.IsCancelled; l.UpdatedAt = r.UpdatedAt;
            }
            else
            {
                db.Reservations.Add(new Reservation
                {
                    SyncId = r.SyncId!, Space = (ReservationSpace)r.Space,
                    RoomNumber = r.RoomNumber, ReservedBy = r.ReservedBy,
                    StartTime = r.StartTime, EndTime = r.EndTime,
                    Notes = r.Notes, CreatedAt = r.CreatedAt,
                    IsCancelled = r.IsCancelled, UpdatedAt = r.UpdatedAt
                });
            }
        }
    }

    private static async Task MergeDeliveries(AppDbContext db, List<DelRow> remote)
    {
        var local = await db.Deliveries.ToDictionaryAsync(x => x.SyncId);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.TryGetValue(r.SyncId!, out var l))
            {
                if (r.UpdatedAt <= l.UpdatedAt) continue;
                l.Type = (DeliveryType)r.Type; l.RoomNumber = r.RoomNumber;
                l.Quantity = r.Quantity; l.ArrivedAt = r.ArrivedAt;
                l.CollectedAt = r.CollectedAt; l.Notes = r.Notes;
                l.UpdatedAt = r.UpdatedAt;
            }
            else
            {
                db.Deliveries.Add(new Delivery
                {
                    SyncId = r.SyncId!, Type = (DeliveryType)r.Type,
                    RoomNumber = r.RoomNumber, Quantity = r.Quantity,
                    ArrivedAt = r.ArrivedAt, CollectedAt = r.CollectedAt,
                    Notes = r.Notes, UpdatedAt = r.UpdatedAt
                });
            }
        }
    }

    private static async Task MergeProducts(AppDbContext db, List<PrdRow> remote)
    {
        var local = await db.Products.ToDictionaryAsync(x => x.SyncId);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.TryGetValue(r.SyncId!, out var l))
            {
                if (r.UpdatedAt <= l.UpdatedAt) continue;
                l.Name = r.Name; l.Type = r.Type; l.Color = r.Color;
                l.SKU = r.Sku; l.UpdatedAt = r.UpdatedAt;
            }
            else
            {
                db.Products.Add(new Product
                {
                    SyncId = r.SyncId!, Name = r.Name, Type = r.Type, Color = r.Color,
                    SKU = r.Sku, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt
                });
            }
        }
    }

    private static async Task MergeSizeVariants(AppDbContext db, List<SvRow> remote)
    {
        var local      = await db.SizeVariants.ToDictionaryAsync(x => x.SyncId);
        var productMap = await db.Products.ToDictionaryAsync(x => x.SyncId, x => x.Id);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.TryGetValue(r.SyncId!, out var l))
            {
                if (r.UpdatedAt <= l.UpdatedAt) continue;
                l.Size = r.Size; l.Quantity = r.Quantity;
                l.MinStockAlert = r.MinStockAlert; l.UpdatedAt = r.UpdatedAt;
            }
            else
            {
                if (string.IsNullOrEmpty(r.ProductSyncId) ||
                    !productMap.TryGetValue(r.ProductSyncId, out var localProductId)) continue;
                db.SizeVariants.Add(new SizeVariant
                {
                    SyncId = r.SyncId!, ProductId = localProductId, Size = r.Size,
                    Quantity = r.Quantity, MinStockAlert = r.MinStockAlert, UpdatedAt = r.UpdatedAt
                });
            }
        }
    }

    private static async Task MergeStockMovements(AppDbContext db, List<SmRow> remote)
    {
        var local  = await db.StockMovements.ToDictionaryAsync(x => x.SyncId);
        var svMap  = await db.SizeVariants.ToDictionaryAsync(x => x.SyncId, x => x.Id);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.ContainsKey(r.SyncId!)) continue; // immutable log — never update
            if (string.IsNullOrEmpty(r.SizeVariantSyncId) ||
                !svMap.TryGetValue(r.SizeVariantSyncId, out var localSvId)) continue;
            db.StockMovements.Add(new StockMovement
            {
                SyncId = r.SyncId!, SizeVariantId = localSvId, ChangeAmount = r.ChangeAmount,
                Reason = (MovementReason)r.Reason, RoomNumber = r.RoomNumber,
                Notes = r.Notes, Date = r.Date, UpdatedAt = r.UpdatedAt
            });
        }
    }

    // ── DTOs (snake_case via JsonNamingPolicy) ────────────────────────────────

    private record PrdRow(
        string? SyncId, string Name, string Type, string Color, string Sku,
        DateTime CreatedAt, DateTime UpdatedAt);

    private record SvRow(
        string? SyncId, string? ProductSyncId, string Size, int Quantity,
        int MinStockAlert, DateTime UpdatedAt);

    private record SmRow(
        string? SyncId, string? SizeVariantSyncId, int ChangeAmount, int Reason,
        string? RoomNumber, string? Notes, DateTime Date, DateTime UpdatedAt);

    private record GiRow(
        string? SyncId, string Name, int TotalQuantity, string? Description, DateTime UpdatedAt);

    private record GilRow(
        string? SyncId, string? GeneralItemSyncId, string RoomNumber, string GivenBy,
        int Quantity, DateTime LoanDate, DateTime? ReturnDate, string? Notes, DateTime UpdatedAt);

    private record RsdRow(
        string? SyncId, string Name, string RoomNumber, string? PhoneNumber,
        bool IsCollaborator, DateTime UpdatedAt);

    private record ResRow(
        string? SyncId, int Space, string RoomNumber, string ReservedBy,
        DateTime StartTime, DateTime EndTime, string? Notes, DateTime CreatedAt,
        bool IsCancelled, DateTime UpdatedAt);

    private record DelRow(
        string? SyncId, int Type, string RoomNumber, int Quantity,
        DateTime ArrivedAt, DateTime? CollectedAt, string? Notes, DateTime UpdatedAt);
}
