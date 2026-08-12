using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AmroStockManager.Services;

public sealed class SupabaseRealtimeService : IAsyncDisposable
{
    // Tables to watch — add any new tables here.
    private static readonly string[] WatchedTables =
    [
        "reservations", "deliveries", "general_item_loans",
        "general_items", "products", "size_variants", "residents"
    ];

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReconnectDelay    = TimeSpan.FromSeconds(5);

    private readonly string _wsUrl;
    private readonly string _apiKey;
    private readonly CacheService _cache;

    private ClientWebSocket? _ws;
    private CancellationTokenSource _cts = new();
    private int _ref = 0;
    private bool _running;

    // Fired on the calling thread (use InvokeAsync + StateHasChanged in subscribers).
    public event Action<string>? TableChanged;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public SupabaseRealtimeService(CacheService cache)
    {
        _cache  = cache;
        _apiKey = AppSecrets.SupabaseKey.Trim();

        // Convert https:// → wss://
        var baseUrl = AppSecrets.SupabaseUrl.Trim().TrimEnd('/');
        _wsUrl = (baseUrl.StartsWith("https://") ? "wss://" + baseUrl[8..] : baseUrl)
                 + $"/realtime/v1/websocket?apikey={Uri.EscapeDataString(_apiKey)}&vsn=1.0.0";
    }

    public async Task StartAsync()
    {
        if (_running) return;
        _running = true;
        _ = RunLoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(new Uri(_wsUrl), ct);

                await JoinChannelAsync(ct);

                using var heartbeat = new PeriodicTimer(HeartbeatInterval);
                var listenTask     = ListenAsync(ct);
                var heartbeatTask  = SendHeartbeatsAsync(heartbeat, ct);

                await Task.WhenAny(listenTask, heartbeatTask);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // swallow — reconnect after delay
            }

            if (!ct.IsCancellationRequested)
                await Task.Delay(ReconnectDelay, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task JoinChannelAsync(CancellationToken ct)
    {
        var postgresChanges = WatchedTables
            .Select(t => new { @event = "*", schema = "public", table = t })
            .ToArray();

        var join = new
        {
            @event = "phx_join",
            topic  = "realtime:public",
            payload = new
            {
                config = new
                {
                    broadcast       = new { self = false },
                    presence        = new { key  = "" },
                    postgres_changes = postgresChanges
                },
                access_token = _apiKey
            },
            @ref = NextRef()
        };

        await SendJsonAsync(join, ct);
    }

    private async Task SendHeartbeatsAsync(PeriodicTimer timer, CancellationToken ct)
    {
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (_ws?.State != WebSocketState.Open) break;
            var hb = new { @event = "heartbeat", topic = "phoenix", payload = new { }, @ref = NextRef() };
            await SendJsonAsync(hb, ct);
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var sb     = new StringBuilder();

        while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            sb.Clear();
            WebSocketReceiveResult result;
            do
            {
                var segment = new ArraySegment<byte>(buffer);
                result = await _ws.ReceiveAsync(segment, ct);
                if (result.MessageType == WebSocketMessageType.Close) return;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            HandleMessage(sb.ToString());
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node?["event"]?.GetValue<string>() != "postgres_changes") return;

            var table = node["payload"]?["data"]?["table"]?.GetValue<string>();
            if (string.IsNullOrEmpty(table)) return;

            // Invalidate the relevant cache entries so the next read is fresh.
            if (table is "residents")                      _cache.Invalidate("residents:all");
            if (table is "general_items" or "general_item_loans") _cache.Invalidate("general_items:all");

            TableChanged?.Invoke(table);
        }
        catch { /* malformed message — ignore */ }
    }

    private async Task SendJsonAsync(object payload, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        await _ws!.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private string NextRef() => Interlocked.Increment(ref _ref).ToString();

    public async ValueTask DisposeAsync()
    {
        _running = false;
        await _cts.CancelAsync();
        _cts.Dispose();
        if (_ws?.State == WebSocketState.Open)
        {
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
            catch { /* ignore */ }
        }
        _ws?.Dispose();
    }
}
