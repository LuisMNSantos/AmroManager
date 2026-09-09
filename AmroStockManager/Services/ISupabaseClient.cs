namespace AmroStockManager.Services;

public interface ISupabaseClient
{
    Task<List<T>> GetAsync<T>(string table, string? query = null);
    Task<int>     GetCountAsync(string table, string? query = null);
    Task<T?>      InsertAsync<T>(string table, object body);
    Task          PatchAsync(string table, string filter, object patch);
    Task          DeleteAsync(string table, string filter);
}
