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

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        InitialiseDatabase(app);
        return app;
    }

    private static void InitialiseDatabase(MauiApp app)
    {
        using var db = app.Services
            .GetRequiredService<IDbContextFactory<AppDbContext>>()
            .CreateDbContext();

        // Create all tables for fresh databases
        db.Database.EnsureCreated();

        // Schema evolution: add columns / tables for existing databases
        try
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE StockMovements ADD COLUMN RoomNumber TEXT");
        }
        catch { /* column already exists — safe to ignore */ }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS GeneralItems (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                Name         TEXT    NOT NULL,
                TotalQuantity INTEGER NOT NULL DEFAULT 1,
                Description  TEXT
            )");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS GeneralItemLoans (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                GeneralItemId   INTEGER NOT NULL
                                    REFERENCES GeneralItems(Id) ON DELETE CASCADE,
                RoomNumber      TEXT NOT NULL,
                GivenBy         TEXT NOT NULL,
                LoanDate        TEXT NOT NULL,
                ReturnDate      TEXT,
                Notes           TEXT
            )");

        // Seed default general item types (once, on first launch)
        if (!db.GeneralItems.Any())
        {
            db.GeneralItems.AddRange(
                new GeneralItem { Name = "Speaker",                  TotalQuantity = 1 },
                new GeneralItem { Name = "Tennis Racket",            TotalQuantity = 2 },
                new GeneralItem { Name = "Clothesline",              TotalQuantity = 3 },
                new GeneralItem { Name = "Access Card – Kitchen",    TotalQuantity = 1 },
                new GeneralItem { Name = "Access Card – Cinema",     TotalQuantity = 1 },
                new GeneralItem { Name = "Cloth Iron",               TotalQuantity = 1 },
                new GeneralItem { Name = "Vroom (Robot Vacuum)",     TotalQuantity = 1 },
                new GeneralItem { Name = "Mop",                      TotalQuantity = 2 },
                new GeneralItem { Name = "Snooker Set",              TotalQuantity = 1 }
            );
            db.SaveChanges();
        }
    }
}
