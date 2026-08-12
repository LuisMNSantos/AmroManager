using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class StockService(SupabaseClient db)
{
    public async Task AdjustStockAsync(string sizeVariantId, int change, MovementReason reason,
                                       string? notes = null, string? roomNumber = null)
    {
        var variants = await db.GetAsync<SizeVariant>("size_variants", $"sync_id=eq.{sizeVariantId}&select=sync_id,quantity");
        if (variants is not [var variant]) return;

        var newQty = Math.Max(0, variant.Quantity + change);
        await db.PatchAsync("size_variants", $"sync_id=eq.{sizeVariantId}", new
        {
            quantity   = newQty,
            updated_at = DateTime.UtcNow
        });

        await db.InsertAsync<StockMovement>("stock_movements", new
        {
            sync_id              = Guid.NewGuid().ToString(),
            size_variant_sync_id = sizeVariantId,
            change_amount        = change,
            reason               = (int)reason,
            room_number          = roomNumber,
            notes                = notes,
            date                 = DateTime.UtcNow,
            updated_at           = DateTime.UtcNow,
            is_deleted           = false
        });
    }

    public async Task<bool> TradeAsync(string outVariantId, string inVariantId,
                                       string roomNumber, string? notes = null)
    {
        var outTask = db.GetAsync<SizeVariant>("size_variants", $"sync_id=eq.{outVariantId}&select=sync_id,quantity");
        var inTask  = db.GetAsync<SizeVariant>("size_variants", $"sync_id=eq.{inVariantId}&select=sync_id,quantity");
        await Task.WhenAll(outTask, inTask);

        if (outTask.Result is not [var outVariant]) return false;
        if (inTask.Result  is not [var inVariant])  return false;
        if (outVariant.Quantity == 0) return false;

        var now = DateTime.UtcNow;

        await db.PatchAsync("size_variants", $"sync_id=eq.{outVariantId}", new { quantity = outVariant.Quantity - 1, updated_at = now });
        await db.PatchAsync("size_variants", $"sync_id=eq.{inVariantId}",  new { quantity = inVariant.Quantity  + 1, updated_at = now });

        await db.InsertAsync<StockMovement>("stock_movements", new
        {
            sync_id              = Guid.NewGuid().ToString(),
            size_variant_sync_id = outVariantId,
            change_amount        = -1,
            reason               = (int)MovementReason.Trade,
            room_number          = roomNumber,
            notes                = string.IsNullOrWhiteSpace(notes) ? "Trade – going out" : $"Trade (out): {notes}",
            date                 = now,
            updated_at           = now,
            is_deleted           = false
        });
        await db.InsertAsync<StockMovement>("stock_movements", new
        {
            sync_id              = Guid.NewGuid().ToString(),
            size_variant_sync_id = inVariantId,
            change_amount        = +1,
            reason               = (int)MovementReason.Trade,
            room_number          = roomNumber,
            notes                = string.IsNullOrWhiteSpace(notes) ? "Trade – coming in" : $"Trade (in): {notes}",
            date                 = now,
            updated_at           = now,
            is_deleted           = false
        });

        return true;
    }

    public async Task<(int totalProducts, int totalUnits, int lowStockCount)> GetDashboardStatsAsync()
    {
        var productsTask = db.GetCountAsync("products",      "is_deleted=eq.false");
        var variantsTask = db.GetAsync<SizeVariant>("size_variants", "is_deleted=eq.false&select=quantity,min_stock_alert");
        await Task.WhenAll(productsTask, variantsTask);

        var variants      = variantsTask.Result;
        var totalUnits    = variants.Sum(sv => sv.Quantity);
        var lowStockCount = variants.Count(sv => sv.MinStockAlert > 0 && sv.Quantity <= sv.MinStockAlert);
        return (productsTask.Result, totalUnits, lowStockCount);
    }

    public async Task<List<StockMovement>> GetRecentMovementsAsync(int count = 15)
    {
        var movements = await db.GetAsync<StockMovement>("stock_movements",
            $"is_deleted=eq.false&order=date.desc&limit={count}");
        if (movements.Count == 0) return movements;

        var variantIds = movements.Select(m => m.SizeVariantId).Distinct();
        var variants   = await db.GetAsync<SizeVariant>("size_variants",
            $"sync_id=in.({string.Join(",", variantIds)})");
        var productIds = variants.Select(v => v.ProductId).Distinct();
        var products   = await db.GetAsync<Product>("products",
            $"sync_id=in.({string.Join(",", productIds)})");

        var productById = products.ToDictionary(p => p.Id);
        var variantById = variants.ToDictionary(v => v.Id);
        foreach (var v in variants)
            v.Product = productById.GetValueOrDefault(v.ProductId) ?? new Product();
        foreach (var m in movements)
            m.SizeVariant = variantById.GetValueOrDefault(m.SizeVariantId) ?? new SizeVariant();
        return movements;
    }

    public async Task<List<SizeVariant>> GetLowStockVariantsAsync()
    {
        var variants = await db.GetAsync<SizeVariant>("size_variants", "is_deleted=eq.false");
        var low = variants.Where(sv => sv.MinStockAlert > 0 && sv.Quantity <= sv.MinStockAlert).ToList();
        if (low.Count == 0) return low;

        var productIds = low.Select(v => v.ProductId).Distinct();
        var products   = await db.GetAsync<Product>("products",
            $"sync_id=in.({string.Join(",", productIds)})");
        var productById = products.ToDictionary(p => p.Id);
        foreach (var v in low)
            v.Product = productById.GetValueOrDefault(v.ProductId) ?? new Product();
        return low.OrderBy(sv => sv.Quantity).ToList();
    }

    public async Task ExportToCsvAsync(string filePath)
    {
        var variantsTask = db.GetAsync<SizeVariant>("size_variants", "is_deleted=eq.false");
        var productsTask = db.GetAsync<Product>("products", "is_deleted=eq.false");
        await Task.WhenAll(variantsTask, productsTask);

        var productById = productsTask.Result.ToDictionary(p => p.Id);
        var variants    = variantsTask.Result;
        foreach (var v in variants)
            v.Product = productById.GetValueOrDefault(v.ProductId) ?? new Product();
        variants = variants.OrderBy(v => v.Product.Name).ThenBy(v => v.Size).ToList();

        var lines = new List<string> { "Product,Type,Color,SKU,Size,Quantity,LowStockThreshold" };
        foreach (var v in variants)
            lines.Add($"\"{v.Product.Name}\",\"{v.Product.Type}\",\"{v.Product.Color}\",\"{v.Product.SKU}\",\"{v.Size}\",{v.Quantity},{v.MinStockAlert}");

        await File.WriteAllLinesAsync(filePath, lines);
    }
}
