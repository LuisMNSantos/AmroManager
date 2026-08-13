# AMRO Porto Manager

A Windows desktop application built to handle the day-to-day operational needs of a residential building. It replaces manual tracking with a structured, intuitive interface used daily by staff to manage inventory, equipment loans, space reservations, and package deliveries.

---

## Features

### 📊 Dashboard
Three-tab overview with proactive alerts and relevant statistics for each area:
- **Produtos** — stock KPIs, low stock table, recent movements
- **Artigos & Reservas** — active loans, today's reservations, overdue access card alerts
- **Encomendas & Cartas** — pending delivery summary and quick-access list
- Proactive alert bar highlights overdue access cards, packages waiting more than 7 days, and low-stock variants

### 🔍 Pesquisa Global
- Search icon in the AppBar opens a debounced cross-table search dialog
- Results grouped into Residents, Pending Deliveries, and Upcoming Reservations
- Clicking any result navigates directly to the relevant page

### 🏠 Vista de Quarto
- Dedicated page at `/quarto/{room}` aggregating everything for a room in one place
- Shows: resident info, pending deliveries (with collect action), active loans (with return action), upcoming reservations, and recent history
- Autocomplete search navigates to the room URL

### 📦 Product Inventory
- Track product stock levels with full size-variant breakdown and movement history
- Low stock alerts configurable per variant
- Stock adjustments and trade-in operations
- CSV export of movement history

### 🪑 General Items & Equipment Loans
- Lend and return items (speakers, irons, rackets, access cards, etc.)
- Linked items support — lending ping pong rackets automatically prompts for ball quantity
- Per-item loan history accessible via dialog

### 📅 Reservation Calendar
- Monthly grid view with colour-coded reservation count badges per day
- Click any day to see the detailed Cozinha MasterChef / Cinema reservation list
- Reservation state machine: **Agendada → Em Curso → Concluída**
- Edit scheduled (not yet activated) reservations directly from the calendar
- Space usage statistics: bar charts showing reservations by weekday and hour (last 90 days)
- Access cards issued only when a reservation is manually activated
- Kitchen time constraint enforced (08:00–22:00)
- Multi-day Cinema reservations supported (e.g. 23:00 day 7 → 02:00 day 8)
- Returning an access card in General Items automatically completes the linked reservation

### 📬 Packages & Letters
- Register incoming packages and letters by room number
- Track collection status with timestamps
- Summary KPIs: pending packages, pending letters, arrived today
- Collapsible history of collected items

### 👥 Resident Management
- Add and edit individual residents inline — no need to reimport the full CSV for updates
- CSV bulk import replaces the entire list (with confirmation)
- Per-resident cascade check before deletion: warns staff if the room has pending deliveries, active loans, or upcoming reservations
- Delete individual residents or clear the entire list

### 🛠️ Maintenance & GDPR
- Preview and soft-delete old records (returned loans, completed reservations, stock movements, collected deliveries) by configurable time period
- Export historical data to CSV before cleanup
- **Resident PII purge**: permanently hard-deletes soft-deleted resident records (name, phone, room) from the database after a configurable period, satisfying GDPR right-to-erasure obligations

---

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | .NET 9 MAUI Blazor Hybrid |
| UI Components | MudBlazor 8 |
| Backend / Database | Supabase (PostgreSQL via PostgREST REST API) |
| Real-time updates | Supabase Realtime (Phoenix WebSocket protocol) |
| Language | C# |
| Platform | Windows 10/11 |

---

## Architecture

- **MAUI Blazor Hybrid** — renders Blazor components inside a native Windows window via WebView2.
- **Always-online** — all data is fetched live from Supabase. No local database or offline cache. An active internet connection is required.
- **SupabaseClient** — a singleton `HttpClient` wrapper that handles Supabase PostgREST REST calls (GET, POST, PATCH, DELETE) with automatic retry on transient failures (up to 2 retries with exponential back-off on 5xx/timeout).
- **SupabaseRealtimeService** — connects to Supabase Realtime via the Phoenix WebSocket protocol, subscribes to `postgres_changes` events on all watched tables, invalidates the in-memory cache, and fires `TableChanged` events that pages subscribe to for live UI refresh.
- **CacheService** — lightweight in-memory TTL cache used for frequently read, rarely changed data (residents: 60 s, general items: 30 s). All write paths explicitly invalidate the relevant cache key.
- **ConnectivityService** — wraps MAUI's `IConnectivity` to surface a real-time connectivity banner when the device loses internet access.
- **Service layer** — each domain area (products, stock, general items, reservations, deliveries, residents, maintenance) has its own injectable singleton service that calls `SupabaseClient` directly.
- **Soft-delete** — all record deletions set `is_deleted = true`. Hard deletes are only performed during the GDPR resident purge.
- **Security** — the admin PIN is stored as a PBKDF2-SHA256 hash (100 000 iterations, random 16-byte salt) in `%LocalAppData%\AmroStockManager\admin.pin`. Verification uses constant-time comparison to prevent timing attacks. Existing plaintext PINs are transparently rehashed on the next successful login.

---

## Getting Started

### Requirements
- Windows 10 version 1903 or later
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (pre-installed on Windows 11)
- Active internet connection (Supabase access required)

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

> **Note:** `AppSecrets.cs` containing the Supabase URL and anon key is not committed to the repository. Create it under `AmroStockManager/` before building:
> ```csharp
> namespace AmroStockManager;
> internal static class AppSecrets
> {
>     public const string SupabaseUrl = "https://your-project.supabase.co";
>     public const string SupabaseKey = "your-anon-key";
> }
> ```

---

## Project Structure

```
AmroStockManager/
├── Components/
│   ├── Layout/
│   │   └── MainLayout.razor          # App shell, nav drawer, connectivity banner, search button
│   └── Pages/                        # All pages and dialogs
│       ├── Dashboard.razor
│       ├── Products.razor
│       ├── GeneralItems.razor
│       ├── Calendar.razor
│       ├── Deliveries.razor
│       ├── RoomView.razor            # /quarto/{room} aggregated view
│       ├── Administration.razor      # Resident management + PIN + maintenance
│       ├── GlobalSearchDialog.razor  # Cross-table search
│       ├── ResidentFormDialog.razor  # Add / edit resident
│       ├── EditReservationDialog.razor
│       ├── MaintenanceDialog.razor
│       └── ...other dialogs
├── Data/
│   └── Models/                       # Plain C# POCOs (no ORM attributes)
│       ├── Product.cs / SizeVariant.cs / StockMovement.cs
│       ├── GeneralItem.cs / GeneralItemLoan.cs
│       ├── Resident.cs
│       ├── Delivery.cs
│       └── Reservation.cs
├── Services/
│   ├── SupabaseClient.cs             # PostgREST HTTP wrapper with retry logic
│   ├── SupabaseRealtimeService.cs    # Phoenix WebSocket Realtime client
│   ├── CacheService.cs               # In-memory TTL cache
│   ├── ConnectivityService.cs        # Network availability wrapper
│   ├── ProductService.cs
│   ├── StockService.cs
│   ├── GeneralItemService.cs
│   ├── ReservationService.cs
│   ├── DeliveryService.cs
│   ├── ResidentService.cs            # Includes PIN hashing (PBKDF2)
│   └── MaintenanceService.cs         # Cleanup + GDPR purge
└── MauiProgram.cs                    # App bootstrap and DI registration
```

---

## License

This project is for personal and internal use. Not licensed for redistribution.

---

*Developed by **Luis Santos** · 2026*
