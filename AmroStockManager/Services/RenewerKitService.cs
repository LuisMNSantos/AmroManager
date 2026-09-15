using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class RenewerKitService(ISupabaseClient db, StockService stock)
{
    // ── Kit template ────────────────────────────────────────────────────────

    public async Task<List<RenewerKitItem>> GetKitItemsAsync()
    {
        var kitItems = await db.GetAsync<RenewerKitItem>("renewer_kit_items", "is_deleted=eq.false");
        if (kitItems.Count == 0) return kitItems;

        var productIds = string.Join(",", kitItems.Select(k => k.ProductId).Distinct());
        var productsTask = db.GetAsync<Product>("products",
            $"sync_id=in.({productIds})&is_deleted=eq.false");
        var variantsTask = db.GetAsync<SizeVariant>("size_variants",
            $"product_sync_id=in.({productIds})&is_deleted=eq.false");
        await Task.WhenAll(productsTask, variantsTask);

        var variantsByProduct = variantsTask.Result
            .GroupBy(v => v.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var productById = productsTask.Result.ToDictionary(p => p.Id);

        foreach (var p in productsTask.Result)
            p.SizeVariants = variantsByProduct.TryGetValue(p.Id, out var vs) ? vs : [];

        foreach (var item in kitItems)
            item.Product = productById.GetValueOrDefault(item.ProductId);

        return kitItems;
    }

    public async Task AddKitItemAsync(string productId)
    {
        var existing = await db.GetAsync<RenewerKitItem>("renewer_kit_items",
            $"product_sync_id=eq.{productId}&is_deleted=eq.false");
        if (existing.Count > 0) return;

        await db.InsertAsync<RenewerKitItem>("renewer_kit_items", new
        {
            sync_id         = Guid.NewGuid().ToString(),
            product_sync_id = productId,
            is_deleted      = false,
            updated_at      = DateTime.UtcNow
        });
    }

    public Task RemoveKitItemAsync(string kitItemId) =>
        db.PatchAsync("renewer_kit_items", $"sync_id=eq.{kitItemId}", new
        {
            is_deleted = true,
            updated_at = DateTime.UtcNow
        });

    // ── Deliveries ───────────────────────────────────────────────────────────

    public async Task<List<RenewerKitDelivery>> GetDeliveriesByYearAsync(int year)
    {
        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var end   = start.AddYears(1);
        var deliveries = await db.GetAsync<RenewerKitDelivery>("renewer_kit_deliveries",
            $"is_deleted=eq.false&delivered_at=gte.{start:O}&delivered_at=lt.{end:O}&order=delivered_at.desc");
        if (deliveries.Count == 0) return deliveries;

        var ids   = string.Join(",", deliveries.Select(d => d.Id));
        var items = await db.GetAsync<RenewerKitDeliveryItem>("renewer_kit_delivery_items",
            $"delivery_sync_id=in.({ids})&is_deleted=eq.false");

        var byDelivery = items.GroupBy(i => i.DeliveryId)
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var d in deliveries)
            d.Items = byDelivery.TryGetValue(d.Id, out var dis) ? dis : [];

        return deliveries;
    }

    public async Task<List<RenewerKitDelivery>> GetAllDeliveriesAsync()
    {
        var deliveriesTask = db.GetAsync<RenewerKitDelivery>("renewer_kit_deliveries",
            "is_deleted=eq.false&order=delivered_at.desc");
        var itemsTask = db.GetAsync<RenewerKitDeliveryItem>("renewer_kit_delivery_items",
            "is_deleted=eq.false");
        await Task.WhenAll(deliveriesTask, itemsTask);

        var byDelivery = itemsTask.Result
            .GroupBy(i => i.DeliveryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var d in deliveriesTask.Result)
            d.Items = byDelivery.TryGetValue(d.Id, out var dis) ? dis : [];

        return deliveriesTask.Result;
    }

    public async Task<List<RenewerKitDelivery>> GetDeliveriesByResidentAsync(string residentId)
    {
        var deliveries = await db.GetAsync<RenewerKitDelivery>("renewer_kit_deliveries",
            $"resident_sync_id=eq.{residentId}&is_deleted=eq.false&order=delivered_at.desc");
        if (deliveries.Count == 0) return deliveries;

        var ids = string.Join(",", deliveries.Select(d => d.Id));
        var items = await db.GetAsync<RenewerKitDeliveryItem>("renewer_kit_delivery_items",
            $"delivery_sync_id=in.({ids})&is_deleted=eq.false");

        var byDelivery = items.GroupBy(i => i.DeliveryId)
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var d in deliveries)
            d.Items = byDelivery.TryGetValue(d.Id, out var dis) ? dis : [];

        return deliveries;
    }

    public Task<int> GetDeliveryCountAsync() =>
        db.GetCountAsync("renewer_kit_deliveries", "is_deleted=eq.false");

    public async Task DeliverKitAsync(
        string residentId, string residentName, string roomNumber,
        string deliveredBy, string? notes,
        List<(string ProductId, string ProductName, string SizeVariantId, string Size)> items)
    {
        var deliveryId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        await db.InsertAsync<RenewerKitDelivery>("renewer_kit_deliveries", new
        {
            sync_id          = deliveryId,
            resident_sync_id = residentId,
            resident_name    = residentName,
            room_number      = roomNumber,
            delivered_by     = deliveredBy,
            delivered_at     = now,
            notes            = string.IsNullOrWhiteSpace(notes) ? (string?)null : notes.Trim(),
            is_deleted       = false,
            updated_at       = now
        });

        foreach (var item in items)
        {
            await db.InsertAsync<RenewerKitDeliveryItem>("renewer_kit_delivery_items", new
            {
                sync_id              = Guid.NewGuid().ToString(),
                delivery_sync_id     = deliveryId,
                product_sync_id      = item.ProductId,
                product_name         = item.ProductName,
                size_variant_sync_id = item.SizeVariantId,
                size                 = item.Size,
                is_deleted           = false,
                updated_at           = now
            });

            await stock.AdjustStockAsync(
                item.SizeVariantId, -1, MovementReason.Distribution,
                $"Kit renovador — {item.ProductName} ({item.Size})",
                roomNumber);
        }
    }
}
