using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class ProductService(SupabaseClient db)
{
    public async Task<List<Product>> GetAllAsync(string? search = null, string? type = null, bool lowStockOnly = false)
    {
        var q = new List<string> { "is_deleted=eq.false", "order=name.asc" };
        if (!string.IsNullOrWhiteSpace(type) && type != "All")
            q.Add($"type=eq.{Uri.EscapeDataString(type)}");

        var productsTask = db.GetAsync<Product>("products", string.Join("&", q));
        var variantsTask = db.GetAsync<SizeVariant>("size_variants", "is_deleted=eq.false");
        await Task.WhenAll(productsTask, variantsTask);

        var products = productsTask.Result;
        var variantsByProduct = variantsTask.Result.GroupBy(v => v.ProductId)
                                                   .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var p in products)
            p.SizeVariants = variantsByProduct.TryGetValue(p.Id, out var vs) ? vs : [];

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            products = products.Where(p =>
                p.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                p.Color.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                p.SKU.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (lowStockOnly)
            products = products.Where(p => p.HasLowStock).ToList();

        return products;
    }

    public async Task<Product?> GetByIdAsync(string id)
    {
        var productsTask = db.GetAsync<Product>("products", $"sync_id=eq.{id}");
        var variantsTask = db.GetAsync<SizeVariant>("size_variants", $"product_sync_id=eq.{id}&is_deleted=eq.false");
        await Task.WhenAll(productsTask, variantsTask);

        var product = productsTask.Result.FirstOrDefault();
        if (product is not null) product.SizeVariants = variantsTask.Result;
        return product;
    }

    public async Task<Product> CreateAsync(Product product)
    {
        product.CreatedAt = DateTime.UtcNow;
        var productSyncId = Guid.NewGuid().ToString();
        var created = await db.InsertAsync<Product>("products", new
        {
            sync_id    = productSyncId,
            name       = product.Name,
            type       = product.Type,
            color      = product.Color,
            sku        = product.SKU,
            created_at = product.CreatedAt,
            updated_at = DateTime.UtcNow,
            is_deleted = false
        });
        product.Id = created!.Id;

        foreach (var sv in product.SizeVariants)
        {
            await db.InsertAsync<SizeVariant>("size_variants", new
            {
                sync_id         = Guid.NewGuid().ToString(),
                product_sync_id = product.Id,
                size            = sv.Size,
                quantity        = sv.Quantity,
                min_stock_alert = sv.MinStockAlert,
                updated_at      = DateTime.UtcNow,
                is_deleted      = false
            });
        }

        return product;
    }

    public async Task UpdateAsync(Product product)
    {
        await db.PatchAsync("products", $"sync_id=eq.{product.Id}", new
        {
            name       = product.Name,
            type       = product.Type,
            color      = product.Color,
            sku        = product.SKU,
            updated_at = DateTime.UtcNow
        });

        var existing = await db.GetAsync<SizeVariant>("size_variants",
            $"product_sync_id=eq.{product.Id}&is_deleted=eq.false&select=sync_id");
        var incomingIds = product.SizeVariants.Where(sv => !string.IsNullOrEmpty(sv.Id))
                                              .Select(sv => sv.Id).ToHashSet();

        foreach (var ev in existing.Where(ev => !incomingIds.Contains(ev.Id)))
        {
            await db.PatchAsync("size_variants",   $"sync_id=eq.{ev.Id}",             new { is_deleted = true, updated_at = DateTime.UtcNow });
            await db.PatchAsync("stock_movements", $"size_variant_sync_id=eq.{ev.Id}", new { is_deleted = true, updated_at = DateTime.UtcNow });
        }

        foreach (var sv in product.SizeVariants)
        {
            if (string.IsNullOrEmpty(sv.Id))
            {
                await db.InsertAsync<SizeVariant>("size_variants", new
                {
                    sync_id         = Guid.NewGuid().ToString(),
                    product_sync_id = product.Id,
                    size            = sv.Size,
                    quantity        = sv.Quantity,
                    min_stock_alert = sv.MinStockAlert,
                    updated_at      = DateTime.UtcNow,
                    is_deleted      = false
                });
            }
            else
            {
                await db.PatchAsync("size_variants", $"sync_id=eq.{sv.Id}", new
                {
                    size            = sv.Size,
                    min_stock_alert = sv.MinStockAlert,
                    updated_at      = DateTime.UtcNow
                });
            }
        }
    }

    public async Task DeleteAsync(string id)
    {
        var variants = await db.GetAsync<SizeVariant>("size_variants", $"product_sync_id=eq.{id}&select=sync_id");
        if (variants.Count > 0)
        {
            var ids = string.Join(",", variants.Select(v => v.Id));
            await db.PatchAsync("stock_movements", $"size_variant_sync_id=in.({ids})",
                new { is_deleted = true, updated_at = DateTime.UtcNow });
        }
        await db.PatchAsync("size_variants", $"product_sync_id=eq.{id}", new { is_deleted = true, updated_at = DateTime.UtcNow });
        await db.PatchAsync("products",      $"sync_id=eq.{id}",          new { is_deleted = true, updated_at = DateTime.UtcNow });
    }

    public static readonly string[] StandardSizes  = ["XS", "S", "M", "L", "XL", "XXL"];
    public static readonly string[] ProductTypes   = ["Sweatshirt", "Shirt", "Hoodie", "T-Shirt", "Jacket", "Other"];

    public static int SizeOrder(string size) => size switch
    {
        "XS" => 0, "S" => 1, "M" => 2, "L" => 3, "XL" => 4, "XXL" => 5, _ => 99
    };
}
