using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using AmroStockManager.Data;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class SyncService(IDbContextFactory<AppDbContext> dbFactory) : IBackgroundSync
{
    private const string LastKey = "sync_last";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling       = JsonNumberHandling.AllowReadingFromString,
        Converters           = { new UtcDateTimeConverter(), new UtcNullableDateTimeConverter() }
    };

    // Supabase returns TIMESTAMPTZ as "...+00:00" which System.Text.Json converts
    // to local time by default — causing a +1h drift on every sync in UTC+1 zones.
    // These converters normalise all datetimes to UTC in both directions.
    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        {
            var v = reader.GetDateTime();
            return v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime();
        }
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions o)
        {
            // EF Core SQLite returns Unspecified kind for values we stored as UTC — treat as UTC.
            var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime()
                                                       : DateTime.SpecifyKind(value, DateTimeKind.Utc);
            writer.WriteStringValue(utc.ToString("O"));
        }
    }

    private sealed class UtcNullableDateTimeConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            var v = reader.GetDateTime();
            return v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime();
        }
        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions o)
        {
            if (value is null) { writer.WriteNullValue(); return; }
            var utc = value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime()
                                                             : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
            writer.WriteStringValue(utc.ToString("O"));
        }
    }

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

    // ── Background trigger (fire-and-forget, skips if already running) ────────

    private int _backgroundSyncing = 0;

    public void TriggerBackgroundSync()
    {
        if (!IsConfigured) return;
        if (Interlocked.CompareExchange(ref _backgroundSyncing, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try { await SyncAsync(); }
            catch { }
            finally { Interlocked.Exchange(ref _backgroundSyncing, 0); }
        });
    }

    // ── Main sync entry point ─────────────────────────────────────────────────

    public async Task<(bool Ok, string Message)> SyncAsync()
    {
        if (!IsConfigured)
            return (false, "Configuração de sincronização em falta.");

        try
        {
            using var http = CreateClient();

            var ping = await http.GetAsync("rest/v1/");
            if (!ping.IsSuccessStatusCode)
                return (false, $"Não foi possível ligar ({Url}). HTTP {(int)ping.StatusCode}.");

            await using var db = await dbFactory.CreateDbContextAsync();

            // ── Upload (local → Supabase) ─────────────────────────────────────
            // IgnoreQueryFilters so soft-deleted records are also uploaded and
            // propagate deletions to other PCs.

            await UpsertRows(http, "products",
                (await db.Products.IgnoreQueryFilters().ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId))
                    .Select(x => new SyncMerge.PrdRow(x.SyncId, x.Name, x.Type, x.Color, x.SKU, x.CreatedAt, x.IsDeleted, x.UpdatedAt)));

            await UpsertRows(http, "size_variants",
                (await db.SizeVariants.IgnoreQueryFilters().Include(sv => sv.Product).ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId) && !string.IsNullOrEmpty(x.Product?.SyncId))
                    .Select(x => new SyncMerge.SvRow(x.SyncId, x.Product!.SyncId, x.Size, x.Quantity, x.MinStockAlert, x.IsDeleted, x.UpdatedAt)));

            await UpsertRows(http, "stock_movements",
                (await db.StockMovements.IgnoreQueryFilters().Include(sm => sm.SizeVariant).ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId) && !string.IsNullOrEmpty(x.SizeVariant?.SyncId))
                    .Select(x => new SyncMerge.SmRow(x.SyncId, x.SizeVariant!.SyncId, x.ChangeAmount, (int)x.Reason,
                                           x.RoomNumber, x.Notes, x.Date, x.IsDeleted, x.UpdatedAt)));

            await UpsertRows(http, "general_items",
                (await db.GeneralItems.IgnoreQueryFilters().ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId))
                    .Select(x => new SyncMerge.GiRow(x.SyncId, x.Name, x.TotalQuantity, x.Description, x.IsDeleted, x.UpdatedAt)));

            await UpsertRows(http, "general_item_loans",
                (await db.GeneralItemLoans.IgnoreQueryFilters().ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId) && !string.IsNullOrEmpty(x.GeneralItemSyncId))
                    .Select(x => new SyncMerge.GilRow(x.SyncId, x.GeneralItemSyncId, x.RoomNumber, x.GivenBy,
                                            x.Quantity, x.LoanDate, x.ReturnDate, x.Notes, x.IsDeleted, x.UpdatedAt)));

            await UpsertRows(http, "residents",
                (await db.Residents.IgnoreQueryFilters().ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId))
                    .Select(x => new SyncMerge.RsdRow(x.SyncId, x.Name, x.RoomNumber, x.PhoneNumber, x.IsCollaborator, x.IsDeleted, x.UpdatedAt)));

            await UpsertRows(http, "reservations",
                (await db.Reservations.IgnoreQueryFilters().ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId))
                    .Select(x => new SyncMerge.ResRow(x.SyncId, (int)x.Space, x.RoomNumber, x.ReservedBy,
                                            x.StartTime, x.EndTime, x.Notes, x.CreatedAt, x.IsCancelled,
                                            x.IsActivated, x.IsCompleted, x.IsDeleted, x.UpdatedAt)));

            await UpsertRows(http, "deliveries",
                (await db.Deliveries.IgnoreQueryFilters().ToListAsync())
                    .Where(x => !string.IsNullOrEmpty(x.SyncId))
                    .Select(x => new SyncMerge.DelRow(x.SyncId, (int)x.Type, x.RoomNumber, x.Quantity,
                                            x.ArrivedAt, x.CollectedAt, x.Notes, x.IsDeleted, x.UpdatedAt)));

            // ── Download (Supabase → local) ───────────────────────────────────

            var remotePrds  = await FetchRows<SyncMerge.PrdRow>(http, "products");
            var remoteSvs   = await FetchRows<SyncMerge.SvRow> (http, "size_variants");
            var remoteSms   = await FetchRows<SyncMerge.SmRow> (http, "stock_movements");
            var remoteItems = await FetchRows<SyncMerge.GiRow> (http, "general_items");
            var remoteLoans = await FetchRows<SyncMerge.GilRow>(http, "general_item_loans");
            var remoteRsds  = await FetchRows<SyncMerge.RsdRow>(http, "residents");
            var remoteRes   = await FetchRows<SyncMerge.ResRow> (http, "reservations");
            var remoteDels  = await FetchRows<SyncMerge.DelRow> (http, "deliveries");

            // Merge in FK-dependency order; IsSyncing suppresses auto-stamping
            db.IsSyncing = true;
            await SyncMerge.MergeProducts       (db, remotePrds);
            await SyncMerge.MergeSizeVariants   (db, remoteSvs);
            await SyncMerge.MergeStockMovements (db, remoteSms);
            await SyncMerge.MergeGeneralItems   (db, remoteItems);
            await SyncMerge.MergeGeneralItemLoans(db, remoteLoans);
            await SyncMerge.MergeResidents      (db, remoteRsds);
            await SyncMerge.MergeReservations   (db, remoteRes);
            await SyncMerge.MergeDeliveries     (db, remoteDels);
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
            throw new InvalidOperationException(
                $"Tabela '{table}' não encontrada no Supabase (HTTP {(int)resp.StatusCode}).");
        return await resp.Content.ReadFromJsonAsync<List<T>>(JsonOpts) ?? [];
    }

}
