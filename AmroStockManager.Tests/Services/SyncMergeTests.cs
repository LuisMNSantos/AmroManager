using AmroStockManager.Data;
using AmroStockManager.Data.Models;
using AmroStockManager.Services;
using AmroStockManager.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AmroStockManager.Tests.Services;

// Helper to make timestamps readable in test declarations
file static class T
{
    public static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
}

public class SyncMergeTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    // Seeds an entity with explicit SyncId/UpdatedAt, bypassing auto-stamp.
    private AppDbContext SyncDb()
    {
        var db = _factory.CreateDbContext();
        db.IsSyncing = true;
        return db;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GeneralItems — covers the 6 core merge scenarios shared by all entities
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GeneralItems_NewRemoteRecord_IsInserted()
    {
        await using var db = SyncDb();
        var remote = new List<SyncMerge.GiRow>
        {
            new("sync-gi-1", "Altifalante", 1, null, false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeGeneralItems(db, remote);
        await db.SaveChangesAsync();

        var item = await db.GeneralItems.IgnoreQueryFilters()
                           .FirstOrDefaultAsync(x => x.SyncId == "sync-gi-1");
        Assert.NotNull(item);
        Assert.Equal("Altifalante", item.Name);
        Assert.Equal(1, item.TotalQuantity);
        Assert.False(item.IsDeleted);
    }

    [Fact]
    public async Task GeneralItems_RemoteNewer_FieldsAreUpdated()
    {
        await using var seedDb = SyncDb();
        seedDb.GeneralItems.Add(new GeneralItem
        {
            SyncId = "sync-gi-2", Name = "Old Name", TotalQuantity = 1,
            UpdatedAt = T.Utc(2024, 1, 1)
        });
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.GiRow>
        {
            new("sync-gi-2", "New Name", 5, "desc", false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeGeneralItems(db, remote);
        await db.SaveChangesAsync();

        var item = await db.GeneralItems.IgnoreQueryFilters()
                           .FirstOrDefaultAsync(x => x.SyncId == "sync-gi-2");
        Assert.Equal("New Name", item!.Name);
        Assert.Equal(5, item.TotalQuantity);
        Assert.Equal("desc", item.Description);
    }

    [Fact]
    public async Task GeneralItems_RemoteOlderOrEqual_NoChange()
    {
        await using var seedDb = SyncDb();
        seedDb.GeneralItems.Add(new GeneralItem
        {
            SyncId = "sync-gi-3", Name = "Current Name", TotalQuantity = 2,
            UpdatedAt = T.Utc(2024, 6, 1)
        });
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.GiRow>
        {
            new("sync-gi-3", "Stale Name", 99, null, false, T.Utc(2024, 1, 1)) // older
        };

        await SyncMerge.MergeGeneralItems(db, remote);
        await db.SaveChangesAsync();

        var item = await db.GeneralItems.IgnoreQueryFilters()
                           .FirstOrDefaultAsync(x => x.SyncId == "sync-gi-3");
        Assert.Equal("Current Name", item!.Name);
        Assert.Equal(2, item.TotalQuantity);
    }

    [Fact]
    public async Task GeneralItems_RemoteSoftDeleted_LocalIsMarkedDeleted()
    {
        await using var seedDb = SyncDb();
        seedDb.GeneralItems.Add(new GeneralItem
        {
            SyncId = "sync-gi-4", Name = "To Delete", TotalQuantity = 1,
            UpdatedAt = T.Utc(2024, 1, 1)
        });
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.GiRow>
        {
            new("sync-gi-4", "To Delete", 1, null, IsDeleted: true, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeGeneralItems(db, remote);
        await db.SaveChangesAsync();

        var item = await db.GeneralItems.IgnoreQueryFilters()
                           .FirstOrDefaultAsync(x => x.SyncId == "sync-gi-4");
        Assert.True(item!.IsDeleted);
    }

    [Fact]
    public async Task GeneralItems_RemoteSoftDeleted_NotFoundLocally_NotInserted()
    {
        await using var db = SyncDb();
        var remote = new List<SyncMerge.GiRow>
        {
            new("sync-gi-ghost", "Ghost", 1, null, IsDeleted: true, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeGeneralItems(db, remote);
        await db.SaveChangesAsync();

        var count = await db.GeneralItems.IgnoreQueryFilters()
                            .CountAsync(x => x.SyncId == "sync-gi-ghost");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GeneralItems_EmptySyncId_IsSkipped()
    {
        await using var db = SyncDb();
        var remote = new List<SyncMerge.GiRow>
        {
            new("", "Should be skipped", 1, null, false, T.Utc(2024, 6, 1)),
            new(null, "Also skipped", 1, null, false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeGeneralItems(db, remote);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.GeneralItems.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task GeneralItems_LocallySoftDeleted_IsVisibleToMerge()
    {
        // A locally-deleted item must still be reachable for merge so remote
        // changes (e.g. a restore) can be applied via IgnoreQueryFilters.
        await using var seedDb = SyncDb();
        seedDb.GeneralItems.Add(new GeneralItem
        {
            SyncId = "sync-gi-5", Name = "Deleted Locally", TotalQuantity = 1,
            IsDeleted = true, UpdatedAt = T.Utc(2024, 1, 1)
        });
        await seedDb.SaveChangesAsync();

        // Normal query must NOT return it
        await using var readDb = _factory.CreateDbContext();
        Assert.Empty(await readDb.GeneralItems.ToListAsync());

        // Merge with newer remote (not deleted) → should update and un-delete
        await using var db = SyncDb();
        var remote = new List<SyncMerge.GiRow>
        {
            new("sync-gi-5", "Restored Name", 3, null, IsDeleted: false, T.Utc(2024, 6, 1))
        };
        await SyncMerge.MergeGeneralItems(db, remote);
        await db.SaveChangesAsync();

        var item = await db.GeneralItems.IgnoreQueryFilters()
                           .FirstOrDefaultAsync(x => x.SyncId == "sync-gi-5");
        Assert.False(item!.IsDeleted);
        Assert.Equal("Restored Name", item.Name);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GeneralItemLoans — FK resolution via GeneralItemSyncId
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GeneralItemLoans_NewRecord_LinkedByGeneralItemSyncId_IsInserted()
    {
        await using var seedDb = SyncDb();
        var item = new GeneralItem
        {
            SyncId = "sync-item-A", Name = "Altifalante", TotalQuantity = 1,
            UpdatedAt = T.Utc(2024, 1, 1)
        };
        seedDb.GeneralItems.Add(item);
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var loanDate = T.Utc(2024, 6, 1);
        var remote = new List<SyncMerge.GilRow>
        {
            new("sync-loan-1", "sync-item-A", "101", "Staff", 1, loanDate, null, null, false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeGeneralItemLoans(db, remote);
        await db.SaveChangesAsync();

        var loan = await db.GeneralItemLoans.IgnoreQueryFilters()
                           .FirstOrDefaultAsync(x => x.SyncId == "sync-loan-1");
        Assert.NotNull(loan);
        Assert.Equal("101", loan.RoomNumber);
        Assert.Equal("sync-item-A", loan.GeneralItemSyncId);
    }

    [Fact]
    public async Task GeneralItemLoans_UnknownGeneralItemSyncId_IsSkipped()
    {
        await using var db = SyncDb();
        var remote = new List<SyncMerge.GilRow>
        {
            new("sync-loan-x", "sync-item-UNKNOWN", "101", "Staff", 1,
                T.Utc(2024, 6, 1), null, null, false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeGeneralItemLoans(db, remote);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.GeneralItemLoans.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task GeneralItemLoans_RemoteNewer_IsUpdated()
    {
        await using var seedDb = SyncDb();
        var item = new GeneralItem
        {
            SyncId = "sync-item-B", Name = "Estendal", TotalQuantity = 2,
            UpdatedAt = T.Utc(2024, 1, 1)
        };
        seedDb.GeneralItems.Add(item);
        await seedDb.SaveChangesAsync();

        await using var seedDb2 = SyncDb();
        seedDb2.GeneralItemLoans.Add(new GeneralItemLoan
        {
            SyncId = "sync-loan-2", GeneralItemId = item.Id, GeneralItemSyncId = "sync-item-B",
            RoomNumber = "101", GivenBy = "Staff", Quantity = 1,
            LoanDate = T.Utc(2024, 1, 1), UpdatedAt = T.Utc(2024, 1, 1)
        });
        await seedDb2.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.GilRow>
        {
            new("sync-loan-2", "sync-item-B", "202", "Manager", 2,
                T.Utc(2024, 6, 1), T.Utc(2024, 6, 15), "returned", false, T.Utc(2024, 6, 15))
        };

        await SyncMerge.MergeGeneralItemLoans(db, remote);
        await db.SaveChangesAsync();

        var loan = await db.GeneralItemLoans.IgnoreQueryFilters()
                           .FirstOrDefaultAsync(x => x.SyncId == "sync-loan-2");
        Assert.Equal("202", loan!.RoomNumber);
        Assert.Equal("Manager", loan.GivenBy);
        Assert.Equal(2, loan.Quantity);
        Assert.NotNull(loan.ReturnDate);
    }

    [Fact]
    public async Task GeneralItemLoans_RemoteSoftDeleted_LocalIsMarkedDeleted()
    {
        await using var seedDb = SyncDb();
        var item = new GeneralItem
        {
            SyncId = "sync-item-C", Name = "Ferro", TotalQuantity = 1,
            UpdatedAt = T.Utc(2024, 1, 1)
        };
        seedDb.GeneralItems.Add(item);
        await seedDb.SaveChangesAsync();

        await using var seedDb2 = SyncDb();
        seedDb2.GeneralItemLoans.Add(new GeneralItemLoan
        {
            SyncId = "sync-loan-3", GeneralItemId = item.Id, GeneralItemSyncId = "sync-item-C",
            RoomNumber = "101", GivenBy = "Staff", Quantity = 1,
            LoanDate = T.Utc(2024, 1, 1), UpdatedAt = T.Utc(2024, 1, 1)
        });
        await seedDb2.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.GilRow>
        {
            new("sync-loan-3", "sync-item-C", "101", "Staff", 1,
                T.Utc(2024, 1, 1), null, null, IsDeleted: true, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeGeneralItemLoans(db, remote);
        await db.SaveChangesAsync();

        var loan = await db.GeneralItemLoans.IgnoreQueryFilters()
                           .FirstOrDefaultAsync(x => x.SyncId == "sync-loan-3");
        Assert.True(loan!.IsDeleted);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Residents
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Residents_NewRecord_IsInserted()
    {
        await using var db = SyncDb();
        var remote = new List<SyncMerge.RsdRow>
        {
            new("sync-res-1", "João Silva", "101", "+351910000001", false, false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeResidents(db, remote);
        await db.SaveChangesAsync();

        var r = await db.Residents.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(x => x.SyncId == "sync-res-1");
        Assert.NotNull(r);
        Assert.Equal("João Silva", r.Name);
        Assert.Equal("101", r.RoomNumber);
    }

    [Fact]
    public async Task Residents_RemoteSoftDeleted_NotFoundLocally_NotInserted()
    {
        await using var db = SyncDb();
        var remote = new List<SyncMerge.RsdRow>
        {
            new("sync-res-ghost", "Ghost", "999", null, false, IsDeleted: true, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeResidents(db, remote);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.Residents.IgnoreQueryFilters()
                                .CountAsync(x => x.SyncId == "sync-res-ghost"));
    }

    [Fact]
    public async Task Residents_RemoteNewer_IsUpdated()
    {
        await using var seedDb = SyncDb();
        seedDb.Residents.Add(new Resident
        {
            SyncId = "sync-res-2", Name = "Old", RoomNumber = "100",
            UpdatedAt = T.Utc(2024, 1, 1)
        });
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.RsdRow>
        {
            new("sync-res-2", "New Name", "202", "+351910000099", true, false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeResidents(db, remote);
        await db.SaveChangesAsync();

        var r = await db.Residents.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(x => x.SyncId == "sync-res-2");
        Assert.Equal("New Name", r!.Name);
        Assert.Equal("202", r.RoomNumber);
        Assert.True(r.IsCollaborator);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Deliveries
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Deliveries_NewRecord_IsInserted()
    {
        await using var db = SyncDb();
        var arrivedAt = T.Utc(2024, 6, 1);
        var remote = new List<SyncMerge.DelRow>
        {
            new("sync-del-1", (int)DeliveryType.Encomenda, "101", 2, arrivedAt, null, null, false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeDeliveries(db, remote);
        await db.SaveChangesAsync();

        var d = await db.Deliveries.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(x => x.SyncId == "sync-del-1");
        Assert.NotNull(d);
        Assert.Equal(DeliveryType.Encomenda, d.Type);
        Assert.Equal("101", d.RoomNumber);
        Assert.Equal(2, d.Quantity);
    }

    [Fact]
    public async Task Deliveries_RemoteSoftDeleted_LocalIsMarkedDeleted()
    {
        await using var seedDb = SyncDb();
        seedDb.Deliveries.Add(new Delivery
        {
            SyncId = "sync-del-2", Type = DeliveryType.Carta, RoomNumber = "202",
            Quantity = 1, ArrivedAt = T.Utc(2024, 1, 1), UpdatedAt = T.Utc(2024, 1, 1)
        });
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.DelRow>
        {
            new("sync-del-2", (int)DeliveryType.Carta, "202", 1,
                T.Utc(2024, 1, 1), null, null, IsDeleted: true, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeDeliveries(db, remote);
        await db.SaveChangesAsync();

        var d = await db.Deliveries.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(x => x.SyncId == "sync-del-2");
        Assert.True(d!.IsDeleted);
    }

    [Fact]
    public async Task Deliveries_RemoteOlderOrEqual_NoChange()
    {
        await using var seedDb = SyncDb();
        seedDb.Deliveries.Add(new Delivery
        {
            SyncId = "sync-del-3", Type = DeliveryType.Encomenda, RoomNumber = "303",
            Quantity = 1, ArrivedAt = T.Utc(2024, 6, 1), UpdatedAt = T.Utc(2024, 6, 1)
        });
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.DelRow>
        {
            new("sync-del-3", (int)DeliveryType.Carta, "999", 99,
                T.Utc(2024, 1, 1), null, null, false, T.Utc(2024, 1, 1)) // older
        };

        await SyncMerge.MergeDeliveries(db, remote);
        await db.SaveChangesAsync();

        var d = await db.Deliveries.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(x => x.SyncId == "sync-del-3");
        Assert.Equal("303", d!.RoomNumber);
        Assert.Equal(1, d.Quantity);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Reservations
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Reservations_NewRecord_IsInserted()
    {
        await using var db = SyncDb();
        var start = T.Utc(2024, 6, 1);
        var end   = T.Utc(2024, 6, 1).AddHours(2);
        var remote = new List<SyncMerge.ResRow>
        {
            new("sync-rsv-1", (int)ReservationSpace.Cinema, "101", "João",
                start, end, null, T.Utc(2024, 5, 31), false, false, false, false, T.Utc(2024, 5, 31))
        };

        await SyncMerge.MergeReservations(db, remote);
        await db.SaveChangesAsync();

        var r = await db.Reservations.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(x => x.SyncId == "sync-rsv-1");
        Assert.NotNull(r);
        Assert.Equal(ReservationSpace.Cinema, r.Space);
        Assert.Equal("101", r.RoomNumber);
        Assert.False(r.IsCancelled);
    }

    [Fact]
    public async Task Reservations_RemoteNewer_CancelledPropagates()
    {
        var start = T.Utc(2024, 6, 1);
        var end   = start.AddHours(2);

        await using var seedDb = SyncDb();
        seedDb.Reservations.Add(new Reservation
        {
            SyncId = "sync-rsv-2", Space = ReservationSpace.Cinema,
            RoomNumber = "101", ReservedBy = "João",
            StartTime = start, EndTime = end, CreatedAt = T.Utc(2024, 5, 31),
            IsCancelled = false, UpdatedAt = T.Utc(2024, 5, 31)
        });
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.ResRow>
        {
            new("sync-rsv-2", (int)ReservationSpace.Cinema, "101", "João",
                start, end, null, T.Utc(2024, 5, 31), IsCancelled: true, false, false, false, T.Utc(2024, 6, 2))
        };

        await SyncMerge.MergeReservations(db, remote);
        await db.SaveChangesAsync();

        var r = await db.Reservations.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(x => x.SyncId == "sync-rsv-2");
        Assert.True(r!.IsCancelled);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Products + SizeVariants + StockMovements (FK chain)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Products_NewRecord_IsInserted()
    {
        await using var db = SyncDb();
        var remote = new List<SyncMerge.PrdRow>
        {
            new("sync-prd-1", "Sweatshirt", "Sweatshirt", "Black", "SW-001",
                T.Utc(2024, 1, 1), false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeProducts(db, remote);
        await db.SaveChangesAsync();

        var p = await db.Products.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(x => x.SyncId == "sync-prd-1");
        Assert.NotNull(p);
        Assert.Equal("Sweatshirt", p.Name);
        Assert.Equal("Black", p.Color);
    }

    [Fact]
    public async Task Products_RemoteSoftDeleted_LocalIsMarkedDeleted()
    {
        await using var seedDb = SyncDb();
        seedDb.Products.Add(new Product
        {
            SyncId = "sync-prd-2", Name = "Old Shirt", Type = "Shirt",
            Color = "White", SKU = "SH-001", CreatedAt = T.Utc(2024, 1, 1),
            UpdatedAt = T.Utc(2024, 1, 1)
        });
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.PrdRow>
        {
            new("sync-prd-2", "Old Shirt", "Shirt", "White", "SH-001",
                T.Utc(2024, 1, 1), IsDeleted: true, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeProducts(db, remote);
        await db.SaveChangesAsync();

        var p = await db.Products.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(x => x.SyncId == "sync-prd-2");
        Assert.True(p!.IsDeleted);
    }

    [Fact]
    public async Task SizeVariants_NewRecord_LinkedByProductSyncId_IsInserted()
    {
        await using var seedDb = SyncDb();
        var product = new Product
        {
            SyncId = "sync-prd-3", Name = "Hoodie", Type = "Hoodie",
            Color = "Blue", SKU = "HD-001", CreatedAt = T.Utc(2024, 1, 1),
            UpdatedAt = T.Utc(2024, 1, 1)
        };
        seedDb.Products.Add(product);
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.SvRow>
        {
            new("sync-sv-1", "sync-prd-3", "M", 10, 5, false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeSizeVariants(db, remote);
        await db.SaveChangesAsync();

        var sv = await db.SizeVariants.IgnoreQueryFilters()
                         .FirstOrDefaultAsync(x => x.SyncId == "sync-sv-1");
        Assert.NotNull(sv);
        Assert.Equal("M", sv.Size);
        Assert.Equal(10, sv.Quantity);
        Assert.Equal(product.Id, sv.ProductId);
    }

    [Fact]
    public async Task SizeVariants_UnknownProductSyncId_IsSkipped()
    {
        await using var db = SyncDb();
        var remote = new List<SyncMerge.SvRow>
        {
            new("sync-sv-x", "sync-prd-UNKNOWN", "L", 5, 2, false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeSizeVariants(db, remote);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.SizeVariants.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task SizeVariants_RemoteNewer_QuantityIsUpdated()
    {
        await using var seedDb = SyncDb();
        var product = new Product
        {
            SyncId = "sync-prd-4", Name = "T-Shirt", Type = "T-Shirt",
            Color = "Red", SKU = "TS-001", CreatedAt = T.Utc(2024, 1, 1),
            UpdatedAt = T.Utc(2024, 1, 1)
        };
        seedDb.Products.Add(product);
        await seedDb.SaveChangesAsync();

        await using var seedDb2 = SyncDb();
        seedDb2.SizeVariants.Add(new SizeVariant
        {
            SyncId = "sync-sv-2", ProductId = product.Id, Size = "S",
            Quantity = 3, MinStockAlert = 5, UpdatedAt = T.Utc(2024, 1, 1)
        });
        await seedDb2.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.SvRow>
        {
            new("sync-sv-2", "sync-prd-4", "S", 20, 3, false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeSizeVariants(db, remote);
        await db.SaveChangesAsync();

        var sv = await db.SizeVariants.IgnoreQueryFilters()
                         .FirstOrDefaultAsync(x => x.SyncId == "sync-sv-2");
        Assert.Equal(20, sv!.Quantity);
        Assert.Equal(3, sv.MinStockAlert);
    }

    [Fact]
    public async Task StockMovements_NewRecord_IsInserted()
    {
        await using var seedDb = SyncDb();
        var product = new Product
        {
            SyncId = "sync-prd-5", Name = "Jacket", Type = "Jacket",
            Color = "Green", SKU = "JK-001", CreatedAt = T.Utc(2024, 1, 1),
            UpdatedAt = T.Utc(2024, 1, 1)
        };
        seedDb.Products.Add(product);
        await seedDb.SaveChangesAsync();

        await using var seedDb2 = SyncDb();
        var sv = new SizeVariant
        {
            SyncId = "sync-sv-3", ProductId = product.Id, Size = "L",
            Quantity = 10, MinStockAlert = 2, UpdatedAt = T.Utc(2024, 1, 1)
        };
        seedDb2.SizeVariants.Add(sv);
        await seedDb2.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.SmRow>
        {
            new("sync-sm-1", "sync-sv-3", -1, (int)MovementReason.Sale,
                "101", null, T.Utc(2024, 6, 1), false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeStockMovements(db, remote);
        await db.SaveChangesAsync();

        var sm = await db.StockMovements.IgnoreQueryFilters()
                         .FirstOrDefaultAsync(x => x.SyncId == "sync-sm-1");
        Assert.NotNull(sm);
        Assert.Equal(-1, sm.ChangeAmount);
        Assert.Equal(MovementReason.Sale, sm.Reason);
        Assert.Equal(sv.Id, sm.SizeVariantId);
    }

    [Fact]
    public async Task StockMovements_ExistingRecord_IsImmutable_NeverUpdated()
    {
        await using var seedDb = SyncDb();
        var product = new Product
        {
            SyncId = "sync-prd-6", Name = "Shirt", Type = "Shirt",
            Color = "Navy", SKU = "SH-002", CreatedAt = T.Utc(2024, 1, 1),
            UpdatedAt = T.Utc(2024, 1, 1)
        };
        seedDb.Products.Add(product);
        await seedDb.SaveChangesAsync();

        await using var seedDb2 = SyncDb();
        var sv = new SizeVariant
        {
            SyncId = "sync-sv-4", ProductId = product.Id, Size = "M",
            Quantity = 5, MinStockAlert = 2, UpdatedAt = T.Utc(2024, 1, 1)
        };
        seedDb2.SizeVariants.Add(sv);
        await seedDb2.SaveChangesAsync();

        await using var seedDb3 = SyncDb();
        seedDb3.StockMovements.Add(new StockMovement
        {
            SyncId = "sync-sm-2", SizeVariantId = sv.Id, ChangeAmount = 10,
            Reason = MovementReason.Restock, Date = T.Utc(2024, 1, 1),
            UpdatedAt = T.Utc(2024, 1, 1)
        });
        await seedDb3.SaveChangesAsync();

        await using var db = SyncDb();
        // Remote has a newer timestamp — but stock movements are immutable
        var remote = new List<SyncMerge.SmRow>
        {
            new("sync-sm-2", "sync-sv-4", 999, (int)MovementReason.Adjustment,
                null, null, T.Utc(2024, 6, 1), false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeStockMovements(db, remote);
        await db.SaveChangesAsync();

        var sm = await db.StockMovements.IgnoreQueryFilters()
                         .FirstOrDefaultAsync(x => x.SyncId == "sync-sm-2");
        Assert.Equal(10, sm!.ChangeAmount); // unchanged
        Assert.Equal(MovementReason.Restock, sm.Reason); // unchanged
    }

    [Fact]
    public async Task StockMovements_RemoteSoftDeleted_NotInserted()
    {
        await using var seedDb = SyncDb();
        var product = new Product
        {
            SyncId = "sync-prd-7", Name = "XShirt", Type = "Shirt",
            Color = "Grey", SKU = "XS-001", CreatedAt = T.Utc(2024, 1, 1),
            UpdatedAt = T.Utc(2024, 1, 1)
        };
        seedDb.Products.Add(product);
        await seedDb.SaveChangesAsync();

        await using var seedDb2 = SyncDb();
        var sv = new SizeVariant
        {
            SyncId = "sync-sv-5", ProductId = product.Id, Size = "XL",
            Quantity = 5, MinStockAlert = 2, UpdatedAt = T.Utc(2024, 1, 1)
        };
        seedDb2.SizeVariants.Add(sv);
        await seedDb2.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.SmRow>
        {
            new("sync-sm-del", "sync-sv-5", -1, (int)MovementReason.Sale,
                null, null, T.Utc(2024, 6, 1), IsDeleted: true, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeStockMovements(db, remote);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.StockMovements.IgnoreQueryFilters()
                                .CountAsync(x => x.SyncId == "sync-sm-del"));
    }

    [Fact]
    public async Task StockMovements_UnknownSizeVariantSyncId_IsSkipped()
    {
        await using var db = SyncDb();
        var remote = new List<SyncMerge.SmRow>
        {
            new("sync-sm-y", "sync-sv-UNKNOWN", -1, (int)MovementReason.Sale,
                null, null, T.Utc(2024, 6, 1), false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeStockMovements(db, remote);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.StockMovements.IgnoreQueryFilters().CountAsync());
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Global query filter — soft-deleted records invisible to normal queries
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SoftDeleted_Records_AreExcludedFromNormalQueries()
    {
        await using var seedDb = SyncDb();
        seedDb.GeneralItems.Add(new GeneralItem
        {
            SyncId = "sync-gi-visible", Name = "Visible", TotalQuantity = 1,
            IsDeleted = false, UpdatedAt = T.Utc(2024, 1, 1)
        });
        seedDb.GeneralItems.Add(new GeneralItem
        {
            SyncId = "sync-gi-hidden", Name = "Hidden", TotalQuantity = 1,
            IsDeleted = true, UpdatedAt = T.Utc(2024, 1, 1)
        });
        seedDb.Residents.Add(new Resident
        {
            SyncId = "sync-res-del", Name = "Deleted Resident", RoomNumber = "999",
            IsDeleted = true, UpdatedAt = T.Utc(2024, 1, 1)
        });
        seedDb.Deliveries.Add(new Delivery
        {
            SyncId = "sync-del-del", Type = DeliveryType.Carta, RoomNumber = "888",
            Quantity = 1, ArrivedAt = T.Utc(2024, 1, 1), IsDeleted = true,
            UpdatedAt = T.Utc(2024, 1, 1)
        });
        await seedDb.SaveChangesAsync();

        await using var db = _factory.CreateDbContext();

        var items     = await db.GeneralItems.ToListAsync();
        var residents = await db.Residents.ToListAsync();
        var deliveries = await db.Deliveries.ToListAsync();

        Assert.Single(items);
        Assert.Equal("Visible", items[0].Name);
        Assert.Empty(residents);
        Assert.Empty(deliveries);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // UTC time-drift regression — simulates the Supabase "+00:00" return format
    // that caused every sync to advance times by the local UTC offset.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Loans_LoanDate_RemainsUtcAfterMerge_NoTimeZoneDrift()
    {
        var originalUtc = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        await using var seedDb = SyncDb();
        var item = new GeneralItem
        {
            SyncId = "sync-item-tz", Name = "Altifalante", TotalQuantity = 1,
            UpdatedAt = T.Utc(2024, 1, 1)
        };
        seedDb.GeneralItems.Add(item);
        await seedDb.SaveChangesAsync();

        await using var seedDb2 = SyncDb();
        seedDb2.GeneralItemLoans.Add(new GeneralItemLoan
        {
            SyncId = "sync-loan-tz", GeneralItemId = item.Id,
            GeneralItemSyncId = "sync-item-tz", RoomNumber = "101", GivenBy = "Staff",
            Quantity = 1, LoanDate = originalUtc, UpdatedAt = T.Utc(2024, 1, 1)
        });
        await seedDb2.SaveChangesAsync();

        // Simulate Supabase returning the date with +00:00 suffix.
        // System.Text.Json converts "2024-06-01T10:00:00+00:00" to local time
        // (e.g. 11:00 in UTC+1) unless the UtcDateTimeConverter is applied.
        // We replicate the exact DateTimeKind.Local value that would be produced.
        var simulatedSupabaseReturn = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc)
            .ToLocalTime(); // same as what System.Text.Json would return without the fix

        await using var db = SyncDb();
        var remote = new List<SyncMerge.GilRow>
        {
            new("sync-loan-tz", "sync-item-tz", "101", "Staff", 1,
                simulatedSupabaseReturn, null, null, false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeGeneralItemLoans(db, remote);
        await db.SaveChangesAsync();

        var loan = await db.GeneralItemLoans.IgnoreQueryFilters()
                           .FirstOrDefaultAsync(x => x.SyncId == "sync-loan-tz");

        // The stored LoanDate must equal the original UTC — not shifted by the UTC offset
        Assert.Equal(originalUtc, loan!.LoanDate.ToUniversalTime());
    }

    [Fact]
    public async Task Deliveries_ArrivedAt_RemainsUtcAfterMerge_NoTimeZoneDrift()
    {
        var originalUtc = new DateTime(2024, 9, 15, 14, 30, 0, DateTimeKind.Utc);

        await using var seedDb = SyncDb();
        seedDb.Deliveries.Add(new Delivery
        {
            SyncId = "sync-del-tz", Type = DeliveryType.Encomenda, RoomNumber = "202",
            Quantity = 1, ArrivedAt = originalUtc, UpdatedAt = T.Utc(2024, 1, 1)
        });
        await seedDb.SaveChangesAsync();

        var simulatedSupabaseReturn = originalUtc.ToLocalTime();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.DelRow>
        {
            new("sync-del-tz", (int)DeliveryType.Encomenda, "202", 1,
                simulatedSupabaseReturn, null, null, false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeDeliveries(db, remote);
        await db.SaveChangesAsync();

        var d = await db.Deliveries.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(x => x.SyncId == "sync-del-tz");

        Assert.Equal(originalUtc, d!.ArrivedAt.ToUniversalTime());
    }

    [Fact]
    public async Task Reservations_StartTime_RemainsUtcAfterMerge_NoTimeZoneDrift()
    {
        var originalUtc = new DateTime(2024, 8, 20, 9, 0, 0, DateTimeKind.Utc);

        await using var seedDb = SyncDb();
        seedDb.Reservations.Add(new Reservation
        {
            SyncId = "sync-rsv-tz", Space = ReservationSpace.Cozinha,
            RoomNumber = "303", ReservedBy = "Staff",
            StartTime = originalUtc, EndTime = originalUtc.AddHours(2),
            CreatedAt = T.Utc(2024, 1, 1), UpdatedAt = T.Utc(2024, 1, 1)
        });
        await seedDb.SaveChangesAsync();

        var simulatedStart = originalUtc.ToLocalTime();
        var simulatedEnd   = originalUtc.AddHours(2).ToLocalTime();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.ResRow>
        {
            new("sync-rsv-tz", (int)ReservationSpace.Cozinha, "303", "Staff",
                simulatedStart, simulatedEnd, null, T.Utc(2024, 1, 1),
                false, false, false, false, T.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeReservations(db, remote);
        await db.SaveChangesAsync();

        var r = await db.Reservations.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(x => x.SyncId == "sync-rsv-tz");

        Assert.Equal(originalUtc, r!.StartTime.ToUniversalTime());
        Assert.Equal(originalUtc.AddHours(2), r.EndTime.ToUniversalTime());
    }

    public void Dispose() => _factory.Dispose();
}
