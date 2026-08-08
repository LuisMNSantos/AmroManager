using Microsoft.EntityFrameworkCore;
using AmroStockManager.Data;
using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class ProductService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Product>> GetAllAsync(string? search = null, string? type = null, bool lowStockOnly = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.Products.Include(p => p.SizeVariants).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{search}%") ||
                EF.Functions.Like(p.Color, $"%{search}%") ||
                EF.Functions.Like(p.SKU, $"%{search}%"));

        if (!string.IsNullOrWhiteSpace(type) && type != "All")
            query = query.Where(p => p.Type == type);

        if (lowStockOnly)
            query = query.Where(p => p.SizeVariants.Any(sv => sv.MinStockAlert > 0 && sv.Quantity <= sv.MinStockAlert));

        return await query.OrderBy(p => p.Name).ToListAsync();
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Products.Include(p => p.SizeVariants).FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Product> CreateAsync(Product product)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        product.CreatedAt = DateTime.UtcNow;
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    public async Task UpdateAsync(Product product)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Products
            .Include(p => p.SizeVariants).ThenInclude(sv => sv.StockMovements)
            .FirstOrDefaultAsync(p => p.Id == product.Id);
        if (existing is null) return;

        existing.Name = product.Name;
        existing.Type = product.Type;
        existing.Color = product.Color;
        existing.SKU = product.SKU;

        var incomingIds = product.SizeVariants.Where(sv => sv.Id > 0).Select(sv => sv.Id).ToHashSet();
        foreach (var sv in existing.SizeVariants.Where(sv => !incomingIds.Contains(sv.Id)))
        {
            sv.IsDeleted = true;
            foreach (var sm in sv.StockMovements) sm.IsDeleted = true;
        }

        foreach (var incoming in product.SizeVariants)
        {
            if (incoming.Id == 0)
            {
                existing.SizeVariants.Add(new SizeVariant
                {
                    Size = incoming.Size,
                    MinStockAlert = incoming.MinStockAlert,
                    Quantity = incoming.Quantity
                });
            }
            else
            {
                var ev = existing.SizeVariants.FirstOrDefault(sv => sv.Id == incoming.Id);
                if (ev is not null)
                {
                    ev.Size = incoming.Size;
                    ev.MinStockAlert = incoming.MinStockAlert;
                }
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var product = await db.Products
            .Include(p => p.SizeVariants).ThenInclude(sv => sv.StockMovements)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return;
        product.IsDeleted = true;
        foreach (var sv in product.SizeVariants)
        {
            sv.IsDeleted = true;
            foreach (var sm in sv.StockMovements) sm.IsDeleted = true;
        }
        await db.SaveChangesAsync();
    }

    public static readonly string[] StandardSizes = ["XS", "S", "M", "L", "XL", "XXL"];
    public static readonly string[] ProductTypes = ["Sweatshirt", "Shirt", "Hoodie", "T-Shirt", "Jacket", "Other"];

    public static int SizeOrder(string size) => size switch
    {
        "XS" => 0, "S" => 1, "M" => 2, "L" => 3, "XL" => 4, "XXL" => 5, _ => 99
    };
}
