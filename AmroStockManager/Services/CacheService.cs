namespace AmroStockManager.Services;

public sealed class CacheService
{
    private sealed record Entry(object Data, DateTime Expires);
    private readonly Dictionary<string, Entry> _store = [];
    private readonly Lock _lock = new();

    public async Task<T> GetOrFetchAsync<T>(string key, Func<Task<T>> fetch, TimeSpan ttl)
    {
        lock (_lock)
        {
            if (_store.TryGetValue(key, out var hit) && hit.Expires > DateTime.UtcNow)
                return (T)hit.Data;
        }

        var data = await fetch();

        lock (_lock)
        {
            _store[key] = new Entry(data!, DateTime.UtcNow + ttl);
        }

        return data;
    }

    public void Invalidate(string key)
    {
        lock (_lock) { _store.Remove(key); }
    }
}
