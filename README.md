# AMRO Porto Manager

A Windows desktop application built to handle the day-to-day operational needs of a residential building. It replaces manual tracking with a structured, intuitive interface used daily by staff to manage inventory, equipment loans, space reservations, and package deliveries.

---

## Features

### 📦 Product Inventory
- Track product stock levels with full movement history
- Low stock alerts on the dashboard
- Stock adjustments and trade-in operations
- CSV export of movement history

### 🪑 General Items & Equipment Loans
- Lend and return items (speakers, irons, rackets, access cards, etc.)
- Linked items support — lending ping pong rackets automatically prompts for ball quantity
- Per-item loan history accessible via dialog
- Delete items from the catalogue

### 📅 Reservation Calendar
- Monthly grid view with colour-coded reservation count badges per day
- Click any day to see the detailed Cozinha MasterChef / Cinema reservation view
- Reservation state machine: **Agendada → Em Curso → Concluída**
- Access cards are only issued when the user manually activates a reservation
- Kitchen time constraint enforced (08:00–22:00)
- Multi-day Cinema reservations supported (e.g. 23:00 day 7 → 02:00 day 8)
- Returning an access card in General Items automatically completes the linked reservation

### 📬 Packages & Letters
- Register incoming packages and letters by room number
- Track collection status with timestamps
- Summary KPIs: pending packages, pending letters, arrived today, awaiting collection
- Collapsible history of collected items

### 📊 Dashboard
Three-tab overview with relevant statistics for each area:
- **Produtos** — stock KPIs, low stock table, recent movements
- **Artigos & Reservas** — active loans, today's reservations, card status
- **Encomendas & Cartas** — pending delivery summary and quick-access list

### 🛠️ Maintenance
- Preview and clean up old records (returned loans, completed reservations, stock movements, collected deliveries) by configurable time period
- Export historical data to CSV before cleanup
- Automatic database compaction (VACUUM) after cleanup

### 💾 Automatic Backup
- On every app launch, a consistent database backup is created using SQLite's `VACUUM INTO`
- Keeps the last 7 copies automatically
- Saves to **OneDrive** if detected, otherwise to **Documents/AmroStock_Backups/**
- Manual backup and folder shortcut available in the Maintenance dialog

---

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | .NET 9 MAUI Blazor Hybrid |
| UI Components | MudBlazor 8 |
| Database | SQLite (local) |
| ORM | Entity Framework Core 9 |
| Language | C# |
| Platform | Windows 10/11 |

---

## Architecture

- **MAUI Blazor Hybrid** — renders Blazor components inside a native Windows window via WebView2, giving a native app feel with web UI flexibility.
- **Local SQLite database** — stored in `%LocalAppData%\AmroStockManager\stock.db`, accessed via `IDbContextFactory` for safe concurrent access.
- **Schema evolution** — handled with raw `ALTER TABLE` and `CREATE TABLE IF NOT EXISTS` statements on startup, avoiding EF migrations on a deployed local database.
- **Service layer** — each domain area (products, stock, general items, reservations, deliveries, maintenance, backup) has its own injectable singleton service.

---

## Getting Started

### Requirements
- Windows 10 version 1903 or later
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (pre-installed on Windows 11)

### Installation
1. Download `AmroStockManager-win-x64.zip` from the [Releases](../../releases) page
2. Extract to any folder
3. Run `AmroStockManager.exe`

The database is created automatically on first launch and pre-seeded with default items. Backups start from the first run.

### Building from Source
```bash
git clone https://github.com/your-username/AMRO_StockManager.git
cd AMRO_StockManager
dotnet build AmroStockManager/AmroStockManager.csproj --framework net9.0-windows10.0.19041.0
```

To publish a self-contained executable:
```bash
dotnet publish AmroStockManager/AmroStockManager.csproj \
  --framework net9.0-windows10.0.19041.0 \
  -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true
```

---

## Project Structure

```
AmroStockManager/
├── Components/
│   ├── Layout/          # MainLayout, navigation drawer
│   └── Pages/           # All pages and dialogs
│       ├── Dashboard.razor
│       ├── Products.razor
│       ├── GeneralItems.razor
│       ├── Calendar.razor
│       ├── Deliveries.razor
│       └── ...dialogs
├── Data/
│   ├── Models/          # EF Core entity models
│   └── AppDbContext.cs
├── Services/            # Business logic layer
│   ├── ProductService.cs
│   ├── GeneralItemService.cs
│   ├── ReservationService.cs
│   ├── DeliveryService.cs
│   ├── MaintenanceService.cs
│   └── BackupService.cs
└── MauiProgram.cs       # App bootstrap, DI, DB init & schema evolution
```

---

## License

This project is for personal and internal use. Not licensed for redistribution.

---

*Developed by **Luis Santos** · 2026*
