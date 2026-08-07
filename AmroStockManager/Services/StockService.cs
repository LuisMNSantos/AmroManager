using Microsoft.EntityFrameworkCore;
using AmroStockManager.Data;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class StockService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task AdjustStockAsync(int sizeVariantId, int change, MovementReason reason, string? notes = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var variant = await db.SizeVariants.FindAsync(sizeVariantId);
        if (variant is null) return;

        variant.Quantity = Math.Max(0, variant.Quantity + change);
        db.StockMovements.Add(new StockMovement
        {
            SizeVariantId = sizeVariantId,
            ChangeAmount = change,
            Reason = reason,
            Notes = notes,
            Date = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task<(int totalProducts, int totalUnits, int lowStockCount)> GetDashboardStatsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var totalProducts = await db.Products.CountAsync();
        var totalUnits = await db.SizeVariants.SumAsync(sv => (int?)sv.Quantity) ?? 0;
        var lowStockCount = await db.SizeVariants
            .CountAsync(sv => sv.MinStockAlert > 0 && sv.Quantity <= sv.MinStockAlert);
        return (totalProducts, totalUnits, lowStockCount);
    }

    public async Task<List<StockMovement>> GetRecentMovementsAsync(int count = 15)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.StockMovements
            .Include(sm => sm.SizeVariant).ThenInclude(sv => sv.Product)
            .OrderByDescending(sm => sm.Date)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<SizeVariant>> GetLowStockVariantsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SizeVariants
            .Include(sv => sv.Product)
            .Where(sv => sv.MinStockAlert > 0 && sv.Quantity <= sv.MinStockAlert)
            .OrderBy(sv => sv.Quantity)
            .ToListAsync();
    }

    public async Task ExportToCsvAsync(string filePath)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var variants = await db.SizeVariants
            .Include(sv => sv.Product)
            .OrderBy(sv => sv.Product.Name).ThenBy(sv => sv.Size)
            .ToListAsync();

        var lines = new List<string> { "Product,Type,Color,SKU,Size,Quantity,LowStockThreshold" };
        foreach (var v in variants)
            lines.Add($"\"{v.Product.Name}\",\"{v.Product.Type}\",\"{v.Product.Color}\",\"{v.Product.SKU}\",\"{v.Size}\",{v.Quantity},{v.MinStockAlert}");

        await File.WriteAllLinesAsync(filePath, lines);
    }
}
