using Microsoft.EntityFrameworkCore;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<SizeVariant> SizeVariants => Set<SizeVariant>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

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
    }
}
