using Microsoft.EntityFrameworkCore;
using AmroStockManager.Data;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

// DTOs and merge logic extracted here so the test project can link this file
// without pulling in SyncService's HTTP/MAUI dependencies.

internal static class SyncMerge
{
    // ── DTOs (snake_case via JsonNamingPolicy in SyncService) ─────────────────

    internal record GiRow(
        string? SyncId, string Name, int TotalQuantity, string? Description,
        bool IsDeleted, DateTime UpdatedAt);

    internal record GilRow(
        string? SyncId, string? GeneralItemSyncId, string RoomNumber, string GivenBy,
        int Quantity, DateTime LoanDate, DateTime? ReturnDate, string? Notes,
        bool IsDeleted, DateTime UpdatedAt);

    internal record RsdRow(
        string? SyncId, string Name, string RoomNumber, string? PhoneNumber,
        bool IsCollaborator, bool IsDeleted, DateTime UpdatedAt);

    internal record ResRow(
        string? SyncId, int Space, string RoomNumber, string ReservedBy,
        DateTime StartTime, DateTime EndTime, string? Notes, DateTime CreatedAt,
        bool IsCancelled, bool IsActivated, bool IsCompleted, bool IsDeleted, DateTime UpdatedAt);

    internal record DelRow(
        string? SyncId, int Type, string RoomNumber, int Quantity,
        DateTime ArrivedAt, DateTime? CollectedAt, string? Notes,
        bool IsDeleted, DateTime UpdatedAt);

    internal record PrdRow(
        string? SyncId, string Name, string Type, string Color, string Sku,
        DateTime CreatedAt, bool IsDeleted, DateTime UpdatedAt);

    internal record SvRow(
        string? SyncId, string? ProductSyncId, string Size, int Quantity,
        int MinStockAlert, bool IsDeleted, DateTime UpdatedAt);

    internal record SmRow(
        string? SyncId, string? SizeVariantSyncId, int ChangeAmount, int Reason,
        string? RoomNumber, string? Notes, DateTime Date, bool IsDeleted, DateTime UpdatedAt);

    // ── Merge methods — SyncId-only, no name-based fallback ──────────────────

    internal static async Task MergeGeneralItems(AppDbContext db, List<GiRow> remote)
    {
        var local = await db.GeneralItems.IgnoreQueryFilters().ToDictionaryAsync(x => x.SyncId);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.TryGetValue(r.SyncId!, out var l))
            {
                if (r.UpdatedAt <= l.UpdatedAt) continue;
                l.IsDeleted = r.IsDeleted;
                if (!r.IsDeleted)
                {
                    l.Name = r.Name; l.TotalQuantity = r.TotalQuantity;
                    l.Description = r.Description;
                }
                l.UpdatedAt = r.UpdatedAt;
            }
            else if (!r.IsDeleted)
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

    internal static async Task MergeGeneralItemLoans(AppDbContext db, List<GilRow> remote)
    {
        var local   = await db.GeneralItemLoans.IgnoreQueryFilters().ToDictionaryAsync(x => x.SyncId);
        var itemMap = await db.GeneralItems.IgnoreQueryFilters().ToDictionaryAsync(x => x.SyncId, x => x.Id);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.TryGetValue(r.SyncId!, out var l))
            {
                if (r.UpdatedAt <= l.UpdatedAt) continue;
                l.IsDeleted = r.IsDeleted;
                if (!r.IsDeleted)
                {
                    l.RoomNumber = r.RoomNumber; l.GivenBy = r.GivenBy;
                    l.Quantity = r.Quantity; l.LoanDate = r.LoanDate;
                    l.ReturnDate = r.ReturnDate; l.Notes = r.Notes;
                }
                l.UpdatedAt = r.UpdatedAt;
            }
            else if (!r.IsDeleted)
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

    internal static async Task MergeResidents(AppDbContext db, List<RsdRow> remote)
    {
        var local = await db.Residents.IgnoreQueryFilters().ToDictionaryAsync(x => x.SyncId);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.TryGetValue(r.SyncId!, out var l))
            {
                if (r.UpdatedAt <= l.UpdatedAt) continue;
                l.IsDeleted = r.IsDeleted;
                if (!r.IsDeleted)
                {
                    l.Name = r.Name; l.RoomNumber = r.RoomNumber;
                    l.PhoneNumber = r.PhoneNumber; l.IsCollaborator = r.IsCollaborator;
                }
                l.UpdatedAt = r.UpdatedAt;
            }
            else if (!r.IsDeleted)
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

    internal static async Task MergeReservations(AppDbContext db, List<ResRow> remote)
    {
        var local = await db.Reservations.IgnoreQueryFilters().ToDictionaryAsync(x => x.SyncId);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.TryGetValue(r.SyncId!, out var l))
            {
                if (r.UpdatedAt <= l.UpdatedAt) continue;
                l.IsDeleted = r.IsDeleted;
                if (!r.IsDeleted)
                {
                    l.Space = (ReservationSpace)r.Space; l.RoomNumber = r.RoomNumber;
                    l.ReservedBy = r.ReservedBy; l.StartTime = r.StartTime;
                    l.EndTime = r.EndTime; l.Notes = r.Notes; l.IsCancelled = r.IsCancelled;
                    l.IsActivated = r.IsActivated; l.IsCompleted = r.IsCompleted;
                }
                l.UpdatedAt = r.UpdatedAt;
            }
            else if (!r.IsDeleted)
            {
                db.Reservations.Add(new Reservation
                {
                    SyncId = r.SyncId!, Space = (ReservationSpace)r.Space,
                    RoomNumber = r.RoomNumber, ReservedBy = r.ReservedBy,
                    StartTime = r.StartTime, EndTime = r.EndTime,
                    Notes = r.Notes, CreatedAt = r.CreatedAt,
                    IsCancelled = r.IsCancelled, IsActivated = r.IsActivated,
                    IsCompleted = r.IsCompleted, UpdatedAt = r.UpdatedAt
                });
            }
        }
    }

    internal static async Task MergeDeliveries(AppDbContext db, List<DelRow> remote)
    {
        var local = await db.Deliveries.IgnoreQueryFilters().ToDictionaryAsync(x => x.SyncId);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.TryGetValue(r.SyncId!, out var l))
            {
                if (r.UpdatedAt <= l.UpdatedAt) continue;
                l.IsDeleted = r.IsDeleted;
                if (!r.IsDeleted)
                {
                    l.Type = (DeliveryType)r.Type; l.RoomNumber = r.RoomNumber;
                    l.Quantity = r.Quantity; l.ArrivedAt = r.ArrivedAt;
                    l.CollectedAt = r.CollectedAt; l.Notes = r.Notes;
                }
                l.UpdatedAt = r.UpdatedAt;
            }
            else if (!r.IsDeleted)
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

    internal static async Task MergeProducts(AppDbContext db, List<PrdRow> remote)
    {
        var local = await db.Products.IgnoreQueryFilters().ToDictionaryAsync(x => x.SyncId);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.TryGetValue(r.SyncId!, out var l))
            {
                if (r.UpdatedAt <= l.UpdatedAt) continue;
                l.IsDeleted = r.IsDeleted;
                if (!r.IsDeleted)
                {
                    l.Name = r.Name; l.Type = r.Type; l.Color = r.Color;
                    l.SKU = r.Sku;
                }
                l.UpdatedAt = r.UpdatedAt;
            }
            else if (!r.IsDeleted)
            {
                db.Products.Add(new Product
                {
                    SyncId = r.SyncId!, Name = r.Name, Type = r.Type, Color = r.Color,
                    SKU = r.Sku, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt
                });
            }
        }
    }

    internal static async Task MergeSizeVariants(AppDbContext db, List<SvRow> remote)
    {
        var local      = await db.SizeVariants.IgnoreQueryFilters().ToDictionaryAsync(x => x.SyncId);
        var productMap = await db.Products.IgnoreQueryFilters().ToDictionaryAsync(x => x.SyncId, x => x.Id);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.TryGetValue(r.SyncId!, out var l))
            {
                if (r.UpdatedAt <= l.UpdatedAt) continue;
                l.IsDeleted = r.IsDeleted;
                if (!r.IsDeleted)
                {
                    l.Size = r.Size; l.Quantity = r.Quantity; l.MinStockAlert = r.MinStockAlert;
                }
                l.UpdatedAt = r.UpdatedAt;
            }
            else if (!r.IsDeleted)
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

    internal static async Task MergeStockMovements(AppDbContext db, List<SmRow> remote)
    {
        var local = await db.StockMovements.IgnoreQueryFilters().ToDictionaryAsync(x => x.SyncId);
        var svMap = await db.SizeVariants.IgnoreQueryFilters().ToDictionaryAsync(x => x.SyncId, x => x.Id);
        foreach (var r in remote.Where(r => !string.IsNullOrEmpty(r.SyncId)))
        {
            if (local.ContainsKey(r.SyncId!)) continue; // immutable log — never update
            if (r.IsDeleted) continue;
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
}
