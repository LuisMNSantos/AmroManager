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
        var resp = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
        await EnsureSuccessAsync(resp, url);
        return await resp.Content.ReadFromJsonAsync<List<T>>(_opts) ?? [];
    }

    public async Task<int> GetCountAsync(string table, string? query = null)
    {
        // limit=0 → PostgREST returns no rows but still sets Content-Range with the total
        var q   = query is not null ? $"{query}&limit=0" : "limit=0";
        var url = $"rest/v1/{table}?{q}";
        var resp = await SendWithRetryAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Prefer", "count=exact");
            return req;
        });
        await EnsureSuccessAsync(resp, url);

        // PostgREST returns "Content-Range: 0-24/573" or "*/0" — no unit prefix, so
        // .NET's ContentRangeHeaderValue always fails to parse it. Read the raw string.
        IEnumerable<string>? headerVals = null;
        resp.Content.Headers.TryGetValues("Content-Range", out headerVals);
        if (headerVals is null) resp.Headers.TryGetValues("Content-Range", out headerVals);

        var raw   = headerVals?.FirstOrDefault();
        var slash = raw?.IndexOf('/') ?? -1;
        return slash >= 0 && int.TryParse(raw![(slash + 1)..], out var count) ? count : 0;
    }

    public async Task<T?> InsertAsync<T>(string table, object body)
    {
        var resp = await SendWithRetryAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"rest/v1/{table}")
            {
                Content = JsonContent.Create(body, options: _opts)
            };
            req.Headers.Add("Prefer", "return=representation");
            return req;
        }, retryOn5xx: false); // writes are not safe to retry automatically
        await EnsureSuccessAsync(resp, $"POST {table}");
        var list = await resp.Content.ReadFromJsonAsync<List<T>>(_opts);
        return list is { Count: > 0 } ? list[0] : default;
    }

    public async Task PatchAsync(string table, string filter, object patch)
    {
        var resp = await SendWithRetryAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Patch, $"rest/v1/{table}?{filter}")
            {
                Content = JsonContent.Create(patch, options: _opts)
            };
            req.Headers.Add("Prefer", "return=minimal");
            return req;
        }, retryOn5xx: false); // writes are not safe to retry automatically
        await EnsureSuccessAsync(resp, $"PATCH {table}?{filter}");
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> buildRequest, bool retryOn5xx = true)
    {
        const int maxRetries = 2;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var resp = await _http.SendAsync(buildRequest());
                if (retryOn5xx && (int)resp.StatusCode >= 500 && attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt)));
                    continue;
                }
                return resp;
            }
            catch (TaskCanceledException) when (attempt < maxRetries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt)));
            }
            catch (HttpRequestException) when (attempt < maxRetries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt)));
            }
        }
        // final attempt — let any exception propagate
        return await _http.SendAsync(buildRequest());
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
