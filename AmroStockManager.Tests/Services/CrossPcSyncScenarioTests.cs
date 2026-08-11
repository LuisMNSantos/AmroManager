using AmroStockManager.Data;
using AmroStockManager.Data.Models;
using AmroStockManager.Services;
using AmroStockManager.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AmroStockManager.Tests.Services;

// Simulates real two-PC sync flows: PC1 performs an action, uploads to Supabase,
// then PC2 merges the Supabase payload — verifying the final state on PC2 is correct.

file static class TS
{
    public static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}

public class CrossPcSyncScenarioTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    private AppDbContext SyncDb()
    {
        var db = _factory.CreateDbContext();
        db.IsSyncing = true;
        return db;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Idempotency — merging the same payload twice must not duplicate records
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GeneralItems_SamePayloadAppliedTwice_NoDuplicates()
    {
        var remote = new List<SyncMerge.GiRow>
        {
            new("sync-gi-idem", "Estendal", 3, null, false, TS.Utc(2024, 6, 1))
        };

        for (var i = 0; i < 2; i++)
        {
            await using var db = SyncDb();
            await SyncMerge.MergeGeneralItems(db, remote);
            await db.SaveChangesAsync();
        }

        await using var readDb = _factory.CreateDbContext();
        Assert.Equal(1, await readDb.GeneralItems.IgnoreQueryFilters()
                                    .CountAsync(x => x.SyncId == "sync-gi-idem"));
    }

    [Fact]
    public async Task Loans_SamePayloadAppliedTwice_NoDuplicates()
    {
        await using var seedDb = SyncDb();
        seedDb.GeneralItems.Add(new GeneralItem
        {
            SyncId = "sync-item-idem", Name = "Aspirador", TotalQuantity = 1,
            UpdatedAt = TS.Utc(2024, 1, 1)
        });
        await seedDb.SaveChangesAsync();

        var remote = new List<SyncMerge.GilRow>
        {
            new("sync-loan-idem", "sync-item-idem", "201", "Staff", 1,
                TS.Utc(2024, 6, 1), null, null, false, TS.Utc(2024, 6, 1))
        };

        for (var i = 0; i < 2; i++)
        {
            await using var db = SyncDb();
            await SyncMerge.MergeGeneralItemLoans(db, remote);
            await db.SaveChangesAsync();
        }

        await using var readDb = _factory.CreateDbContext();
        Assert.Equal(1, await readDb.GeneralItemLoans.IgnoreQueryFilters()
                                    .CountAsync(x => x.SyncId == "sync-loan-idem"));
    }

    [Fact]
    public async Task Reservations_SamePayloadAppliedTwice_NoDuplicates()
    {
        var start = TS.Utc(2024, 6, 1, 10);
        var remote = new List<SyncMerge.ResRow>
        {
            new("sync-rsv-idem", (int)ReservationSpace.Cinema, "101", "João",
                start, start.AddHours(2), null, TS.Utc(2024, 5, 31),
                false, false, false, false, TS.Utc(2024, 5, 31))
        };

        for (var i = 0; i < 2; i++)
        {
            await using var db = SyncDb();
            await SyncMerge.MergeReservations(db, remote);
            await db.SaveChangesAsync();
        }

        await using var readDb = _factory.CreateDbContext();
        Assert.Equal(1, await readDb.Reservations.IgnoreQueryFilters()
                                    .CountAsync(x => x.SyncId == "sync-rsv-idem"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Conflict resolution — same record edited on both PCs; equal or older remote
    // timestamp must never overwrite local data (last writer wins by UpdatedAt)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Residents_EqualTimestamp_LocalIsPreserved()
    {
        await using var seedDb = SyncDb();
        seedDb.Residents.Add(new Resident
        {
            SyncId = "sync-res-tie", Name = "Local Version", RoomNumber = "100",
            UpdatedAt = TS.Utc(2024, 6, 1)
        });
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.RsdRow>
        {
            new("sync-res-tie", "Remote Version", "200", null, false, false, TS.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeResidents(db, remote);
        await db.SaveChangesAsync();

        await using var readDb = _factory.CreateDbContext();
        var r = await readDb.Residents.IgnoreQueryFilters().FirstAsync(x => x.SyncId == "sync-res-tie");
        Assert.Equal("Local Version", r.Name);
        Assert.Equal("100", r.RoomNumber);
    }

    [Fact]
    public async Task GeneralItems_EqualTimestamp_LocalIsPreserved()
    {
        await using var seedDb = SyncDb();
        seedDb.GeneralItems.Add(new GeneralItem
        {
            SyncId = "sync-gi-tie", Name = "Local", TotalQuantity = 5,
            UpdatedAt = TS.Utc(2024, 6, 1)
        });
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.GiRow>
        {
            new("sync-gi-tie", "Remote", 99, null, false, TS.Utc(2024, 6, 1))
        };

        await SyncMerge.MergeGeneralItems(db, remote);
        await db.SaveChangesAsync();

        await using var readDb = _factory.CreateDbContext();
        var item = await readDb.GeneralItems.IgnoreQueryFilters().FirstAsync(x => x.SyncId == "sync-gi-tie");
        Assert.Equal("Local", item.Name);
        Assert.Equal(5, item.TotalQuantity);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Reservation state — cross-PC propagation of IsActivated / IsCompleted
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Reservation_ActivatedOnPC1_IsActivatedPropagatesOnPC2()
    {
        var start = TS.Utc(2024, 6, 1, 10);
        var created = TS.Utc(2024, 5, 30);

        await using var seedDb = SyncDb();
        seedDb.Reservations.Add(new Reservation
        {
            SyncId = "sync-rsv-act", Space = ReservationSpace.Cozinha,
            RoomNumber = "101", ReservedBy = "Ana",
            StartTime = start, EndTime = start.AddHours(2), CreatedAt = created,
            IsActivated = false, IsCompleted = false, IsCancelled = false,
            UpdatedAt = created
        });
        await seedDb.SaveChangesAsync();

        // PC1 activated: Supabase now has IsActivated=true
        await using var db = SyncDb();
        var remote = new List<SyncMerge.ResRow>
        {
            new("sync-rsv-act", (int)ReservationSpace.Cozinha, "101", "Ana",
                start, start.AddHours(2), null, created,
                IsCancelled: false, IsActivated: true, IsCompleted: false,
                IsDeleted: false, UpdatedAt: TS.Utc(2024, 6, 1, 10))
        };

        await SyncMerge.MergeReservations(db, remote);
        await db.SaveChangesAsync();

        await using var readDb = _factory.CreateDbContext();
        var r = await readDb.Reservations.IgnoreQueryFilters().FirstAsync(x => x.SyncId == "sync-rsv-act");
        Assert.True(r.IsActivated);
        Assert.False(r.IsCompleted);
        Assert.False(r.IsCancelled);
    }

    [Fact]
    public async Task Reservation_CompletedOnPC1_IsCompletedPropagatesOnPC2()
    {
        var start = TS.Utc(2024, 6, 1, 10);
        var created = TS.Utc(2024, 5, 30);

        await using var seedDb = SyncDb();
        seedDb.Reservations.Add(new Reservation
        {
            SyncId = "sync-rsv-done", Space = ReservationSpace.Cinema,
            RoomNumber = "202", ReservedBy = "Carlos",
            StartTime = start, EndTime = start.AddHours(2), CreatedAt = created,
            IsActivated = true, IsCompleted = false,
            UpdatedAt = TS.Utc(2024, 6, 1, 10)
        });
        await seedDb.SaveChangesAsync();

        // PC1 completed (card returned): IsCompleted=true
        await using var db = SyncDb();
        var remote = new List<SyncMerge.ResRow>
        {
            new("sync-rsv-done", (int)ReservationSpace.Cinema, "202", "Carlos",
                start, start.AddHours(2), null, created,
                IsCancelled: false, IsActivated: true, IsCompleted: true,
                IsDeleted: false, UpdatedAt: TS.Utc(2024, 6, 1, 12))
        };

        await SyncMerge.MergeReservations(db, remote);
        await db.SaveChangesAsync();

        await using var readDb = _factory.CreateDbContext();
        var r = await readDb.Reservations.IgnoreQueryFilters().FirstAsync(x => x.SyncId == "sync-rsv-done");
        Assert.True(r.IsActivated);
        Assert.True(r.IsCompleted);
    }

    [Fact]
    public async Task Reservation_CancelledOnPC1_WhilePC2HadActivated_NewerCancelWins()
    {
        // PC2 activated at 10:00, PC1 cancelled at 10:05 — PC1 is newer so cancel wins
        var start = TS.Utc(2024, 6, 1, 10);
        var created = TS.Utc(2024, 5, 30);

        await using var seedDb = SyncDb();
        seedDb.Reservations.Add(new Reservation
        {
            SyncId = "sync-rsv-conflict", Space = ReservationSpace.Cinema,
            RoomNumber = "303", ReservedBy = "Maria",
            StartTime = start, EndTime = start.AddHours(2), CreatedAt = created,
            IsActivated = true, IsCompleted = false, IsCancelled = false,
            UpdatedAt = TS.Utc(2024, 6, 1, 10)
        });
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.ResRow>
        {
            new("sync-rsv-conflict", (int)ReservationSpace.Cinema, "303", "Maria",
                start, start.AddHours(2), null, created,
                IsCancelled: true, IsActivated: false, IsCompleted: false,
                IsDeleted: false, UpdatedAt: TS.Utc(2024, 6, 1, 10, 5))
        };

        await SyncMerge.MergeReservations(db, remote);
        await db.SaveChangesAsync();

        await using var readDb = _factory.CreateDbContext();
        var r = await readDb.Reservations.IgnoreQueryFilters().FirstAsync(x => x.SyncId == "sync-rsv-conflict");
        Assert.True(r.IsCancelled);
        Assert.False(r.IsActivated);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Delivery collected on PC1 → CollectedAt propagates to PC2
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delivery_CollectedOnPC1_CollectedAtPropagatesOnPC2()
    {
        var arrivedAt = TS.Utc(2024, 6, 1);
        var collectedAt = TS.Utc(2024, 6, 2, 14);

        await using var seedDb = SyncDb();
        seedDb.Deliveries.Add(new Delivery
        {
            SyncId = "sync-del-coll", Type = DeliveryType.Encomenda,
            RoomNumber = "101", Quantity = 1, ArrivedAt = arrivedAt,
            CollectedAt = null, UpdatedAt = arrivedAt
        });
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.DelRow>
        {
            new("sync-del-coll", (int)DeliveryType.Encomenda, "101", 1,
                arrivedAt, collectedAt, null, false, collectedAt)
        };

        await SyncMerge.MergeDeliveries(db, remote);
        await db.SaveChangesAsync();

        await using var readDb = _factory.CreateDbContext();
        var d = await readDb.Deliveries.IgnoreQueryFilters().FirstAsync(x => x.SyncId == "sync-del-coll");
        Assert.NotNull(d.CollectedAt);
        // EF Core SQLite returns DateTime as Unspecified — SpecifyKind(Utc) normalises without shifting
        Assert.Equal(collectedAt, DateTime.SpecifyKind(d.CollectedAt!.Value, DateTimeKind.Utc));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Loan returned on PC1 → ReturnDate propagates to PC2
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Loan_ReturnedOnPC1_ReturnDatePropagatesOnPC2()
    {
        var loanDate = TS.Utc(2024, 6, 1);
        var returnDate = TS.Utc(2024, 6, 3, 16);

        await using var seedDb = SyncDb();
        var item = new GeneralItem
        {
            SyncId = "sync-item-ret", Name = "Ferro", TotalQuantity = 1,
            UpdatedAt = TS.Utc(2024, 1, 1)
        };
        seedDb.GeneralItems.Add(item);
        await seedDb.SaveChangesAsync();

        await using var seedDb2 = SyncDb();
        seedDb2.GeneralItemLoans.Add(new GeneralItemLoan
        {
            SyncId = "sync-loan-ret", GeneralItemId = item.Id,
            GeneralItemSyncId = "sync-item-ret", RoomNumber = "202",
            GivenBy = "Staff", Quantity = 1, LoanDate = loanDate,
            ReturnDate = null, UpdatedAt = loanDate
        });
        await seedDb2.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.GilRow>
        {
            new("sync-loan-ret", "sync-item-ret", "202", "Staff", 1,
                loanDate, returnDate, null, false, returnDate)
        };

        await SyncMerge.MergeGeneralItemLoans(db, remote);
        await db.SaveChangesAsync();

        await using var readDb = _factory.CreateDbContext();
        var loan = await readDb.GeneralItemLoans.IgnoreQueryFilters().FirstAsync(x => x.SyncId == "sync-loan-ret");
        Assert.NotNull(loan.ReturnDate);
        // EF Core SQLite returns DateTime as Unspecified — SpecifyKind(Utc) normalises without shifting
        Assert.Equal(returnDate, DateTime.SpecifyKind(loan.ReturnDate!.Value, DateTimeKind.Utc));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Delete propagation — PC1 soft-deletes item + its loan, PC2 has both active
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ItemAndLoan_SoftDeletedOnPC1_BothMarkDeletedOnPC2()
    {
        var loanDate = TS.Utc(2024, 6, 1);
        var deletedAt = TS.Utc(2024, 6, 5);

        await using var seedDb = SyncDb();
        var item = new GeneralItem
        {
            SyncId = "sync-item-del2", Name = "Altifalante", TotalQuantity = 1,
            IsDeleted = false, UpdatedAt = TS.Utc(2024, 1, 1)
        };
        seedDb.GeneralItems.Add(item);
        await seedDb.SaveChangesAsync();

        await using var seedDb2 = SyncDb();
        seedDb2.GeneralItemLoans.Add(new GeneralItemLoan
        {
            SyncId = "sync-loan-del2", GeneralItemId = item.Id,
            GeneralItemSyncId = "sync-item-del2", RoomNumber = "101",
            GivenBy = "Staff", Quantity = 1, LoanDate = loanDate,
            IsDeleted = false, UpdatedAt = loanDate
        });
        await seedDb2.SaveChangesAsync();

        await using var db = SyncDb();
        var remoteItems = new List<SyncMerge.GiRow>
        {
            new("sync-item-del2", "Altifalante", 1, null, IsDeleted: true, deletedAt)
        };
        var remoteLoans = new List<SyncMerge.GilRow>
        {
            new("sync-loan-del2", "sync-item-del2", "101", "Staff", 1,
                loanDate, null, null, IsDeleted: true, deletedAt)
        };

        await SyncMerge.MergeGeneralItems(db, remoteItems);
        await SyncMerge.MergeGeneralItemLoans(db, remoteLoans);
        await db.SaveChangesAsync();

        await using var readDb = _factory.CreateDbContext();
        var dbItem = await readDb.GeneralItems.IgnoreQueryFilters().FirstAsync(x => x.SyncId == "sync-item-del2");
        var dbLoan = await readDb.GeneralItemLoans.IgnoreQueryFilters().FirstAsync(x => x.SyncId == "sync-loan-del2");
        Assert.True(dbItem.IsDeleted);
        Assert.True(dbLoan.IsDeleted);
        Assert.Empty(await readDb.GeneralItems.ToListAsync());
        Assert.Empty(await readDb.GeneralItemLoans.ToListAsync());
    }

    // ══════════════════════════════════════════════════════════════════════════
    // FK ordering — loan arrives before its parent item is known on PC2.
    // First sync skips it; second sync (item present) inserts it.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Loan_ArrivesBeforeParentItem_SkippedThenInsertedOnNextSync()
    {
        var loanDate = TS.Utc(2024, 6, 1);
        var loanRemote = new List<SyncMerge.GilRow>
        {
            new("sync-loan-fk", "sync-item-fk-missing", "101", "Staff", 1,
                loanDate, null, null, false, loanDate)
        };

        // First sync: item not yet present — loan must be skipped
        await using (var db = SyncDb())
        {
            await SyncMerge.MergeGeneralItemLoans(db, loanRemote);
            await db.SaveChangesAsync();
        }

        await using var readDb1 = _factory.CreateDbContext();
        Assert.Equal(0, await readDb1.GeneralItemLoans.IgnoreQueryFilters().CountAsync());

        // Second sync: parent item arrives first, then loan is retried
        await using (var db = SyncDb())
        {
            var itemRemote = new List<SyncMerge.GiRow>
            {
                new("sync-item-fk-missing", "Estendal", 2, null, false, TS.Utc(2024, 1, 1))
            };
            await SyncMerge.MergeGeneralItems(db, itemRemote);
            await db.SaveChangesAsync();
        }

        await using (var db = SyncDb())
        {
            await SyncMerge.MergeGeneralItemLoans(db, loanRemote);
            await db.SaveChangesAsync();
        }

        await using var readDb2 = _factory.CreateDbContext();
        Assert.Equal(1, await readDb2.GeneralItemLoans.IgnoreQueryFilters()
                                     .CountAsync(x => x.SyncId == "sync-loan-fk"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Full FK chain — Product → SizeVariant → StockMovement in one sync batch
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullProductChain_CreatedOnPC1_AllLinkedCorrectlyOnPC2()
    {
        var created = TS.Utc(2024, 6, 1);

        // Each merge queries the DB for parent FK — intermediate saves are required
        // so the next merge can resolve the parent's local Id.
        await using (var db = SyncDb())
        {
            await SyncMerge.MergeProducts(db, new List<SyncMerge.PrdRow>
            {
                new("sync-prd-chain", "Polo", "Polo", "White", "PL-001", created, false, created)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = SyncDb())
        {
            await SyncMerge.MergeSizeVariants(db, new List<SyncMerge.SvRow>
            {
                new("sync-sv-chain", "sync-prd-chain", "L", 15, 3, false, created)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = SyncDb())
        {
            await SyncMerge.MergeStockMovements(db, new List<SyncMerge.SmRow>
            {
                new("sync-sm-chain", "sync-sv-chain", 15, (int)MovementReason.Restock,
                    null, null, created, false, created)
            });
            await db.SaveChangesAsync();
        }

        await using var readDb = _factory.CreateDbContext();
        var product  = await readDb.Products.IgnoreQueryFilters().FirstAsync(x => x.SyncId == "sync-prd-chain");
        var variant  = await readDb.SizeVariants.IgnoreQueryFilters().FirstAsync(x => x.SyncId == "sync-sv-chain");
        var movement = await readDb.StockMovements.IgnoreQueryFilters().FirstAsync(x => x.SyncId == "sync-sm-chain");

        Assert.Equal(product.Id, variant.ProductId);
        Assert.Equal(variant.Id, movement.SizeVariantId);
        Assert.Equal(15, movement.ChangeAmount);
        Assert.Equal(MovementReason.Restock, movement.Reason);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Mixed batch — new + updated records in the same merge call
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Residents_MixedBatch_NewAndUpdated_AllCorrect()
    {
        await using var seedDb = SyncDb();
        seedDb.Residents.Add(new Resident
        {
            SyncId = "sync-res-existing", Name = "Old Name", RoomNumber = "100",
            UpdatedAt = TS.Utc(2024, 1, 1)
        });
        await seedDb.SaveChangesAsync();

        await using var db = SyncDb();
        var remote = new List<SyncMerge.RsdRow>
        {
            new("sync-res-existing", "Updated Name", "101", "+351910000001", false, false, TS.Utc(2024, 6, 1)),
            new("sync-res-brand-new", "New Person", "202", null, true, false, TS.Utc(2024, 6, 1)),
        };

        await SyncMerge.MergeResidents(db, remote);
        await db.SaveChangesAsync();

        await using var readDb = _factory.CreateDbContext();
        var all = await readDb.Residents.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, all.Count);

        var existing = all.First(r => r.SyncId == "sync-res-existing");
        Assert.Equal("Updated Name", existing.Name);
        Assert.Equal("101", existing.RoomNumber);

        var brand = all.First(r => r.SyncId == "sync-res-brand-new");
        Assert.Equal("New Person", brand.Name);
        Assert.True(brand.IsCollaborator);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Restore — a record deleted on PC1 is restored on PC2 with a newer timestamp
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GeneralItem_DeletedOnPC1_RestoredByPC2_LatestTimestampWins()
    {
        // Start: PC2 received a soft-delete from PC1 (IsDeleted=true, T=day 1)
        await using var seedDb = SyncDb();
        seedDb.GeneralItems.Add(new GeneralItem
        {
            SyncId = "sync-gi-restore", Name = "Mop", TotalQuantity = 2,
            IsDeleted = true, UpdatedAt = TS.Utc(2024, 6, 1)
        });
        await seedDb.SaveChangesAsync();

        // PC2 restores it locally and uploads (T=day 2). Now Supabase has IsDeleted=false.
        await using var db = SyncDb();
        var remote = new List<SyncMerge.GiRow>
        {
            new("sync-gi-restore", "Mop", 2, null, IsDeleted: false, TS.Utc(2024, 6, 2))
        };

        await SyncMerge.MergeGeneralItems(db, remote);
        await db.SaveChangesAsync();

        await using var readDb = _factory.CreateDbContext();
        var item = await readDb.GeneralItems.IgnoreQueryFilters().FirstAsync(x => x.SyncId == "sync-gi-restore");
        Assert.False(item.IsDeleted);
        Assert.Single(await readDb.GeneralItems.ToListAsync());
    }

    public void Dispose() => _factory.Dispose();
}
