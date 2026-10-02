namespace AmroStockManager.Services;

public sealed class WhatsAppService
{
    private double _areaLeft, _areaTop, _areaWidth, _areaHeight;

    public string? PendingUrl { get; private set; }

    // First load or explicit navigation with URL — sets Source + bounds + visible.
    public event Action<string, double, double, double, double>? LoadRequested;
    // Returning to /whatsapp with session already alive — only repositions + shows.
    public event Action<double, double, double, double>? ShowRequested;
    public event Action? CloseRequested;
    public event Action? NavigateToWhatsAppRequested;

    private bool _isInitialized;

    public void SetContentArea(double left, double top, double width, double height)
    {
        _areaLeft = left; _areaTop = top; _areaWidth = width; _areaHeight = height;
    }

    public void Open(string? phone = null, string? prefilledText = null)
    {
        PendingUrl = BuildUrl(phone, prefilledText);
        MainThread.BeginInvokeOnMainThread(() => NavigateToWhatsAppRequested?.Invoke());
    }

    public void OpenAt(string? phone, string? prefilledText, double left, double top, double width, double height)
    {
        bool needsLoad = !_isInitialized || PendingUrl is not null || phone is not null;

        if (needsLoad)
        {
            var url = phone is not null ? BuildUrl(phone, prefilledText) : (PendingUrl ?? "https://web.whatsapp.com");
            PendingUrl = null;
            _isInitialized = true;
            MainThread.BeginInvokeOnMainThread(() => LoadRequested?.Invoke(url, left, top, width, height));
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(() => ShowRequested?.Invoke(left, top, width, height));
        }
    }

    public void Close()
        => MainThread.BeginInvokeOnMainThread(() => CloseRequested?.Invoke());

    public static string BuildUrl(string? phone, string? text = null)
    {
        if (phone is null) return "https://web.whatsapp.com";
        var cleaned = phone.TrimStart('+').Replace(" ", "").Replace("-", "");
        return text is not null
            ? $"https://web.whatsapp.com/send?phone={cleaned}&text={Uri.EscapeDataString(text)}"
            : $"https://web.whatsapp.com/send?phone={cleaned}";
    }
}
