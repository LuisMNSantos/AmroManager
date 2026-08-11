using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using AmroStockManager.Data;
using AmroStockManager.Data.Models;
using AmroStockManager.Services;

namespace AmroStockManager;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices();

        var dbFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AmroStockManager");
        Directory.CreateDirectory(dbFolder);
        var dbPath = Path.Combine(dbFolder, "stock.db");

        builder.Services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        builder.Services.AddSingleton<ProductService>();
        builder.Services.AddSingleton<StockService>();
        builder.Services.AddSingleton<GeneralItemService>();
        builder.Services.AddSingleton<ReservationService>();
        builder.Services.AddSingleton<DeliveryService>();
        builder.Services.AddSingleton<MaintenanceService>();
        builder.Services.AddSingleton<BackupService>();
        builder.Services.AddSingleton<ResidentService>();
        builder.Services.AddSingleton<SyncService>();
        builder.Services.AddSingleton<IBackgroundSync>(sp => sp.GetRequiredService<SyncService>());

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        InitialiseDatabase(app);

        // Startup backup — runs in background so it doesn't delay app launch
        var backupSvc = app.Services.GetRequiredService<BackupService>();
        _ = Task.Run(async () =>
        {
            try { await backupSvc.RunBackupAsync(); }
            catch { /* Never block startup due to backup failure */ }
        });

        // Startup sync — runs in background after backup
        var syncSvc = app.Services.GetRequiredService<SyncService>();
        if (syncSvc.IsConfigured)
        {
            _ = Task.Run(async () =>
            {
                try { await syncSvc.SyncAsync(); }
                catch { /* Never block startup due to sync failure */ }
            });
        }

        return app;
    }

    private static void InitialiseDatabase(MauiApp app)
    {
        using var db = app.Services
            .GetRequiredService<IDbContextFactory<AppDbContext>>()
            .CreateDbContext();

        // Create all tables for fresh databases
        db.Database.EnsureCreated();

        // ── Schema evolution (safe on existing DBs) ──────────────────────────
        try { db.Database.ExecuteSqlRaw("ALTER TABLE StockMovements ADD COLUMN RoomNumber TEXT"); }
        catch { }

        try { db.Database.ExecuteSqlRaw("ALTER TABLE GeneralItemLoans ADD COLUMN Quantity INTEGER NOT NULL DEFAULT 1"); }
        catch { }

        try { db.Database.ExecuteSqlRaw("ALTER TABLE GeneralItems ADD COLUMN LinkedItemId INTEGER"); }
        catch { }

        // ── Sync columns ─────────────────────────────────────────────────────
        // Table/column names cannot be SQL parameters — literal strings are safe here
        // because these are compile-time constants, not user input.
        foreach (var sql in new[]
        {
            "ALTER TABLE GeneralItems      ADD COLUMN IsDeleted  INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE GeneralItemLoans  ADD COLUMN IsDeleted  INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE Residents         ADD COLUMN IsDeleted  INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE Products          ADD COLUMN IsDeleted  INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE SizeVariants      ADD COLUMN IsDeleted  INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE StockMovements    ADD COLUMN IsDeleted  INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE Deliveries        ADD COLUMN IsDeleted  INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE Reservations      ADD COLUMN IsDeleted  INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE GeneralItems      ADD COLUMN SyncId     TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE GeneralItemLoans  ADD COLUMN SyncId     TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE Residents         ADD COLUMN SyncId     TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE Reservations      ADD COLUMN IsActivated INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE Reservations      ADD COLUMN IsCompleted INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE Reservations      ADD COLUMN SyncId     TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE Deliveries        ADD COLUMN SyncId     TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE GeneralItems      ADD COLUMN UpdatedAt  TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'",
            "ALTER TABLE GeneralItemLoans  ADD COLUMN UpdatedAt  TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'",
            "ALTER TABLE Residents         ADD COLUMN UpdatedAt  TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'",
            "ALTER TABLE Reservations      ADD COLUMN UpdatedAt  TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'",
            "ALTER TABLE Deliveries        ADD COLUMN UpdatedAt  TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'",
            "ALTER TABLE GeneralItemLoans  ADD COLUMN GeneralItemSyncId TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE Products          ADD COLUMN SyncId     TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE Products          ADD COLUMN UpdatedAt  TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'",
            "ALTER TABLE SizeVariants      ADD COLUMN SyncId     TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE SizeVariants      ADD COLUMN UpdatedAt  TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'",
            "ALTER TABLE StockMovements    ADD COLUMN SyncId     TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE StockMovements    ADD COLUMN UpdatedAt  TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'",
        })
        { try { db.Database.ExecuteSqlRaw(sql); } catch { } }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS GeneralItems (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                Name          TEXT    NOT NULL,
                TotalQuantity INTEGER NOT NULL DEFAULT 1,
                Description   TEXT,
                LinkedItemId  INTEGER REFERENCES GeneralItems(Id)
            )");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS GeneralItemLoans (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                GeneralItemId INTEGER NOT NULL REFERENCES GeneralItems(Id) ON DELETE CASCADE,
                RoomNumber    TEXT NOT NULL,
                GivenBy       TEXT NOT NULL,
                Quantity      INTEGER NOT NULL DEFAULT 1,
                LoanDate      TEXT NOT NULL,
                ReturnDate    TEXT,
                Notes         TEXT
            )");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS Deliveries (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Type        INTEGER NOT NULL DEFAULT 0,
                RoomNumber  TEXT NOT NULL,
                Quantity    INTEGER NOT NULL DEFAULT 1,
                ArrivedAt   TEXT NOT NULL,
                CollectedAt TEXT,
                Notes       TEXT
            )");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS Reservations (
                Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                Space            INTEGER NOT NULL,
                RoomNumber       TEXT NOT NULL,
                ReservedBy       TEXT NOT NULL,
                StartTime        TEXT NOT NULL,
                EndTime          TEXT NOT NULL,
                Notes            TEXT,
                CreatedAt        TEXT NOT NULL,
                IsCancelled      INTEGER NOT NULL DEFAULT 0,
                AccessCardLoanId INTEGER REFERENCES GeneralItemLoans(Id) ON DELETE SET NULL
            )");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS Residents (
                Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                Name           TEXT    NOT NULL,
                RoomNumber     TEXT    NOT NULL DEFAULT '',
                PhoneNumber    TEXT,
                IsCollaborator INTEGER NOT NULL DEFAULT 0
            )");

        // ── Rename English item names to Portuguese (existing databases) ──────
        db.Database.ExecuteSqlRaw("UPDATE GeneralItems SET Name = 'Altifalante' WHERE Name = 'Speaker'");
        db.Database.ExecuteSqlRaw("UPDATE GeneralItems SET Name = 'Raqueta de Ténis' WHERE Name = 'Tennis Racket'");
        db.Database.ExecuteSqlRaw("UPDATE GeneralItems SET Name = 'Estendal' WHERE Name = 'Clothesline'");
        db.Database.ExecuteSqlRaw("UPDATE GeneralItems SET Name = 'Cartão de Acesso – Cozinha' WHERE Name = 'Access Card – Kitchen'");
        db.Database.ExecuteSqlRaw("UPDATE GeneralItems SET Name = 'Cartão de Acesso – Cinema' WHERE Name = 'Access Card – Cinema'");
        db.Database.ExecuteSqlRaw("UPDATE GeneralItems SET Name = 'Ferro de Engomar' WHERE Name = 'Cloth Iron'");
        db.Database.ExecuteSqlRaw("UPDATE GeneralItems SET Name = 'Aspirador (Vroom)' WHERE Name = 'Vroom (Robot Vacuum)'");
        db.Database.ExecuteSqlRaw("UPDATE GeneralItems SET Name = 'Esfregona' WHERE Name = 'Mop'");
        db.Database.ExecuteSqlRaw("UPDATE GeneralItems SET Name = 'Jogo de Snooker' WHERE Name = 'Snooker Set'");

        // ── Seed default items (fresh install, standalone only) ──────────────
        // Skip seeding when sync is configured — items come from Supabase instead.
        var syncConfigured = !string.IsNullOrWhiteSpace(AppSecrets.SupabaseUrl.Trim());
        if (!db.GeneralItems.Any() && !syncConfigured)
        {
            db.GeneralItems.AddRange(
                new GeneralItem { Name = "Altifalante",              TotalQuantity = 1 },
                new GeneralItem { Name = "Raqueta de Ténis",         TotalQuantity = 2 },
                new GeneralItem { Name = "Estendal",                 TotalQuantity = 3 },
                new GeneralItem { Name = "Cartão de Acesso – Cozinha", TotalQuantity = 1 },
                new GeneralItem { Name = "Cartão de Acesso – Cinema",  TotalQuantity = 1 },
                new GeneralItem { Name = "Ferro de Engomar",         TotalQuantity = 1 },
                new GeneralItem { Name = "Aspirador (Vroom)",        TotalQuantity = 1 },
                new GeneralItem { Name = "Esfregona",                TotalQuantity = 2 },
                new GeneralItem { Name = "Jogo de Snooker",          TotalQuantity = 1 }
            );
            db.SaveChanges();

            // Seed ping pong items with linked relationship
            var balls = new GeneralItem { Name = "Bolas de Ping Pong", TotalQuantity = 6 };
            db.GeneralItems.Add(balls);
            db.SaveChanges();

            db.GeneralItems.Add(new GeneralItem
            {
                Name = "Raquetas de Ping Pong",
                TotalQuantity = 4,
                Description = "Inclui bolas de ping pong",
                LinkedItemId = balls.Id
            });
            db.SaveChanges();
        }

        // ── Seed ping pong items (existing databases without them) ───────────
        if (!db.GeneralItems.Any(gi => gi.Name == "Raquetas de Ping Pong") && !syncConfigured)
        {
            var balls = new GeneralItem { Name = "Bolas de Ping Pong", TotalQuantity = 6 };
            db.GeneralItems.Add(balls);
            db.SaveChanges();

            db.GeneralItems.Add(new GeneralItem
            {
                Name = "Raquetas de Ping Pong",
                TotalQuantity = 4,
                Description = "Inclui bolas de ping pong",
                LinkedItemId = balls.Id
            });
            db.SaveChanges();
        }

        // ── Back-fill IsActivated / IsCompleted on existing reservations ────────
        db.Database.ExecuteSqlRaw(
            "UPDATE Reservations SET IsActivated = 1 WHERE AccessCardLoanId IS NOT NULL AND IsActivated = 0");
        db.Database.ExecuteSqlRaw(@"
            UPDATE Reservations SET IsCompleted = 1
            WHERE AccessCardLoanId IS NOT NULL AND IsCompleted = 0
              AND AccessCardLoanId IN (SELECT Id FROM GeneralItemLoans WHERE ReturnDate IS NOT NULL)");

        // ── Populate SyncId for records created before sync was added ─────────
        PopulateSyncIds(db);
    }

    private static void PopulateSyncIds(AppDbContext db)
    {
        bool any = false;

        foreach (var x in db.Products.Where(x => x.SyncId == "").ToList())       { db.Entry(x).State = EntityState.Modified; any = true; }
        foreach (var x in db.SizeVariants.Where(x => x.SyncId == "").ToList())   { db.Entry(x).State = EntityState.Modified; any = true; }
        foreach (var x in db.StockMovements.Where(x => x.SyncId == "").ToList()) { db.Entry(x).State = EntityState.Modified; any = true; }
        foreach (var x in db.GeneralItems.Where(x => x.SyncId == "").ToList())   { db.Entry(x).State = EntityState.Modified; any = true; }
        foreach (var x in db.GeneralItemLoans.Where(x => x.SyncId == "").ToList()) { db.Entry(x).State = EntityState.Modified; any = true; }
        foreach (var x in db.Residents.Where(x => x.SyncId == "").ToList())      { db.Entry(x).State = EntityState.Modified; any = true; }
        foreach (var x in db.Reservations.Where(x => x.SyncId == "").ToList())   { db.Entry(x).State = EntityState.Modified; any = true; }
        foreach (var x in db.Deliveries.Where(x => x.SyncId == "").ToList())     { db.Entry(x).State = EntityState.Modified; any = true; }

        if (any) db.SaveChanges();

        // Back-fill GeneralItemSyncId on loans that pre-date the field
        var loansWithoutSyncRef = db.GeneralItemLoans
            .Where(l => l.GeneralItemSyncId == "")
            .Join(db.GeneralItems, l => l.GeneralItemId, gi => gi.Id, (l, gi) => new { l, gi })
            .ToList();
        foreach (var pair in loansWithoutSyncRef)
            pair.l.GeneralItemSyncId = pair.gi.SyncId;
        if (loansWithoutSyncRef.Count > 0) db.SaveChanges();
    }
}
