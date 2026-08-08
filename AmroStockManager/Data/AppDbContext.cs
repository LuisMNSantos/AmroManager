using Microsoft.EntityFrameworkCore;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public bool IsSyncing { get; set; }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<SizeVariant> SizeVariants => Set<SizeVariant>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<GeneralItem> GeneralItems => Set<GeneralItem>();
    public DbSet<GeneralItemLoan> GeneralItemLoans => Set<GeneralItemLoan>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<Resident> Residents => Set<Resident>();

    public override int SaveChanges()
    {
        StampSyncables();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampSyncables();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampSyncables()
    {
        if (IsSyncing) return;
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<ISyncable>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            if (string.IsNullOrEmpty(entry.Entity.SyncId))
                entry.Entity.SyncId = Guid.NewGuid().ToString();
            entry.Entity.UpdatedAt = now;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>()
            .HasMany(p => p.SizeVariants)
            .WithOne(sv => sv.Product)
            .HasForeignKey(sv => sv.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SizeVariant>()
            .HasMany(sv => sv.StockMovements)
            .WithOne(sm => sm.SizeVariant)
            .HasForeignKey(sm => sm.SizeVariantId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GeneralItem>()
            .HasMany(gi => gi.Loans)
            .WithOne(l => l.GeneralItem)
            .HasForeignKey(l => l.GeneralItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Reservation>()
            .HasOne(r => r.AccessCardLoan)
            .WithMany()
            .HasForeignKey(r => r.AccessCardLoanId)
            .OnDelete(DeleteBehavior.SetNull);

        // Global soft-delete filters — all queries automatically exclude deleted records
        modelBuilder.Entity<GeneralItem>()     .HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<GeneralItemLoan>() .HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<Resident>()        .HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<Product>()         .HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<SizeVariant>()     .HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<StockMovement>()   .HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<Delivery>()        .HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<Reservation>()     .HasQueryFilter(x => !x.IsDeleted);
    }
}
