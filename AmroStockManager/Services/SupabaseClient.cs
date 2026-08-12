using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmroStockManager.Services;

public class SupabaseClient
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling         = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters             = { new UtcDateTimeConverter(), new UtcNullableDateTimeConverter() }
    };

    private readonly HttpClient _http;

    public SupabaseClient()
    {
        var url = AppSecrets.SupabaseUrl.Trim().TrimEnd('/') + "/";
        var key = AppSecrets.SupabaseKey.Trim();
        _http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Add("apikey", key);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<List<T>> GetAsync<T>(string table, string? query = null)
    {
        var url = query is not null ? $"rest/v1/{table}?{query}" : $"rest/v1/{table}";
        var resp = await _http.GetAsync(url);
        await EnsureSuccessAsync(resp, url);
        return await resp.Content.ReadFromJsonAsync<List<T>>(_opts) ?? [];
    }

    public async Task<int> GetCountAsync(string table, string? query = null)
    {
        var url = query is not null ? $"rest/v1/{table}?{query}" : $"rest/v1/{table}";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Prefer", "count=exact");
        req.Headers.Range = new RangeHeaderValue(0, 0);
        var resp = await _http.SendAsync(req);
        await EnsureSuccessAsync(resp, url);
        var contentRange = resp.Content.Headers.ContentRange;
        if (contentRange?.HasLength == true)
            return (int)contentRange.Length!.Value;
        return 0;
    }

    public async Task<T?> InsertAsync<T>(string table, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"rest/v1/{table}")
        {
            Content = JsonContent.Create(body, options: _opts)
        };
        req.Headers.Add("Prefer", "return=representation");
        var resp = await _http.SendAsync(req);
        await EnsureSuccessAsync(resp, $"POST {table}");
        var list = await resp.Content.ReadFromJsonAsync<List<T>>(_opts);
        return list is { Count: > 0 } ? list[0] : default;
    }

    public async Task PatchAsync(string table, string filter, object patch)
    {
        var req = new HttpRequestMessage(HttpMethod.Patch, $"rest/v1/{table}?{filter}")
        {
            Content = JsonContent.Create(patch, options: _opts)
        };
        req.Headers.Add("Prefer", "return=minimal");
        var resp = await _http.SendAsync(req);
        await EnsureSuccessAsync(resp, $"PATCH {table}?{filter}");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, string context)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"{(int)resp.StatusCode} {resp.ReasonPhrase} [{context}]: {body}");
    }

    // Normalises all DateTimes to UTC (Supabase returns "+00:00" which STJ reads as local)
    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        {
            var v = reader.GetDateTime();
            return v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime();
        }
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions o)
        {
            var utc = value.Kind == DateTimeKind.Local
                ? value.ToUniversalTime()
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
            writer.WriteStringValue(utc.ToString("O"));
        }
    }

    private sealed class UtcNullableDateTimeConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            var v = reader.GetDateTime();
            return v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime();
        }
        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions o)
        {
            if (value is null) { writer.WriteNullValue(); return; }
            var utc = value.Value.Kind == DateTimeKind.Local
                ? value.Value.ToUniversalTime()
                : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
            writer.WriteStringValue(utc.ToString("O"));
        }
    }
}
