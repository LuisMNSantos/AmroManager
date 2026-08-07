using Microsoft.EntityFrameworkCore;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<SizeVariant> SizeVariants => Set<SizeVariant>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<GeneralItem> GeneralItems => Set<GeneralItem>();
    public DbSet<GeneralItemLoan> GeneralItemLoans => Set<GeneralItemLoan>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();

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
    }
}
