using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class DistributionService(SupabaseClient db, CacheService cache, StockService stockSvc)
{
    private static readonly TimeSpan _ttl = TimeSpan.FromSeconds(30);
    private const string _cacheKey = "distributions:all";

    public Task<List<DistributionCampaign>> GetAllAsync() =>
        cache.GetOrFetchAsync(_cacheKey,
            () => db.GetAsync<DistributionCampaign>("distribution_campaigns",
                "is_deleted=eq.false&order=created_at.desc"),
            _ttl);

    public async Task<List<DistributionCampaign>> GetByVariantsAsync(IEnumerable<string> variantIds)
    {
        var ids = string.Join(",", variantIds);
        if (string.IsNullOrEmpty(ids)) return [];
        return await db.GetAsync<DistributionCampaign>("distribution_campaigns",
            $"is_deleted=eq.false&size_variant_sync_id=in.({ids})&order=created_at.desc");
    }

    public async Task<DistributionCampaign?> GetWithRecordsAsync(string campaignId)
    {
        var campaigns = await db.GetAsync<DistributionCampaign>("distribution_campaigns",
            $"sync_id=eq.{campaignId}&is_deleted=eq.false&limit=1");
        var campaign = campaigns.FirstOrDefault();
        if (campaign is null) return null;

        campaign.Records = await db.GetAsync<DistributionRecord>("distribution_records",
            $"campaign_sync_id=eq.{campaignId}&is_deleted=eq.false&order=room_number.asc");
        return campaign;
    }

    public async Task<List<(DistributionRecord Record, DistributionCampaign Campaign)>> GetDeliveredByRoomAsync(string roomNumber)
    {
        var room    = Uri.EscapeDataString(roomNumber.Trim().ToUpper());
        var records = await db.GetAsync<DistributionRecord>("distribution_records",
            $"is_deleted=eq.false&room_number=eq.{room}&distributed_at=not.is.null&order=distributed_at.desc");
        if (records.Count == 0) return [];

        var ids       = string.Join(",", records.Select(r => r.CampaignId).Distinct());
        var campaigns = await db.GetAsync<DistributionCampaign>("distribution_campaigns",
            $"sync_id=in.({ids})&select=sync_id,name,variant_label");
        var byId = campaigns.ToDictionary(c => c.Id);

        return records
            .Where(r => byId.ContainsKey(r.CampaignId))
            .Select(r => (r, byId[r.CampaignId]))
            .ToList();
    }

    public async Task<string> CreateAsync(string name, string sizeVariantId, string variantLabel,
        int quantityPerRoom, string? notes, IEnumerable<string> rooms)
    {
        var roomList = rooms
            .Select(r => r.Trim().ToUpper())
            .Where(r => r.Length > 0)
            .Distinct()
            .OrderBy(r => r)
            .ToList();

        var campaignId = Guid.NewGuid().ToString();
        var now        = DateTime.UtcNow;

        await db.InsertAsync<DistributionCampaign>("distribution_campaigns", new
        {
            sync_id              = campaignId,
            name                 = name.Trim(),
            size_variant_sync_id = sizeVariantId,
            variant_label        = variantLabel,
            quantity_per_room    = quantityPerRoom,
            total_rooms          = roomList.Count,
            delivered_count      = 0,
            notes                = notes,
            created_at           = now,
            is_deleted           = false,
            updated_at           = now
        });

        foreach (var room in roomList)
        {
            await db.InsertAsync<DistributionRecord>("distribution_records", new
            {
                sync_id          = Guid.NewGuid().ToString(),
                campaign_sync_id = campaignId,
                room_number      = room,
                distributed_at   = (DateTime?)null,
                is_deleted       = false,
                updated_at       = now
            });
        }

        cache.Invalidate(_cacheKey);
        return campaignId;
    }

    public async Task DeliverAsync(string recordId, string campaignId, string roomNumber)
    {
        var now = DateTime.UtcNow;

        await db.PatchAsync("distribution_records", $"sync_id=eq.{recordId}", new
        {
            distributed_at = now,
            updated_at     = now
        });

        var campaigns = await db.GetAsync<DistributionCampaign>("distribution_campaigns",
            $"sync_id=eq.{campaignId}&select=sync_id,delivered_count,size_variant_sync_id,quantity_per_room,name&limit=1");
        if (campaigns is [var c])
        {
            await db.PatchAsync("distribution_campaigns", $"sync_id=eq.{campaignId}", new
            {
                delivered_count = c.DeliveredCount + 1,
                updated_at      = now
            });

            await stockSvc.AdjustStockAsync(
                c.SizeVariantId,
                -c.QuantityPerRoom,
                MovementReason.Distribution,
                $"Distribuição: {c.Name}",
                roomNumber);
        }
        cache.Invalidate(_cacheKey);
    }

    public async Task BulkDeliverAsync(IReadOnlyList<string> recordIds, string campaignId)
    {
        if (recordIds.Count == 0) return;

        var now      = DateTime.UtcNow;
        var inClause = string.Join(",", recordIds);
        await db.PatchAsync("distribution_records", $"sync_id=in.({inClause})", new
        {
            distributed_at = now,
            updated_at     = now
        });

        var campaigns = await db.GetAsync<DistributionCampaign>("distribution_campaigns",
            $"sync_id=eq.{campaignId}&select=sync_id,delivered_count,size_variant_sync_id,quantity_per_room,name&limit=1");
        if (campaigns is [var c])
        {
            await db.PatchAsync("distribution_campaigns", $"sync_id=eq.{campaignId}", new
            {
                delivered_count = c.DeliveredCount + recordIds.Count,
                updated_at      = now
            });

            await stockSvc.AdjustStockAsync(
                c.SizeVariantId,
                -(c.QuantityPerRoom * recordIds.Count),
                MovementReason.Distribution,
                $"Distribuição em lote: {c.Name} ({recordIds.Count} quarto{(recordIds.Count != 1 ? "s" : "")})");
        }
        cache.Invalidate(_cacheKey);
    }

    public async Task DeleteAsync(string campaignId)
    {
        var now = DateTime.UtcNow;
        await db.PatchAsync("distribution_campaigns", $"sync_id=eq.{campaignId}", new
        {
            is_deleted = true,
            updated_at = now
        });
        await db.PatchAsync("distribution_records", $"campaign_sync_id=eq.{campaignId}", new
        {
            is_deleted = true,
            updated_at = now
        });
        cache.Invalidate(_cacheKey);
    }
}
