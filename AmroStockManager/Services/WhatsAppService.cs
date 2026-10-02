namespace AmroStockManager.Services;

public sealed class WhatsAppService
{
    private double _areaLeft, _areaTop, _areaWidth, _areaHeight;

    /// <summary>URL to open when the /whatsapp page next renders (set by Open() from other pages).</summary>
    public string? PendingUrl { get; private set; }

    public event Action<string, double, double, double, double>? OpenRequested;
    public event Action? CloseRequested;
    /// <summary>Raised when Open() is called from outside /whatsapp — MainLayout navigates there.</summary>
    public event Action? NavigateToWhatsAppRequested;

    public void SetContentArea(double left, double top, double width, double height)
    {
        _areaLeft = left; _areaTop = top; _areaWidth = width; _areaHeight = height;
    }

    /// <summary>Called from notification buttons (Deliveries, Reservations, etc.).</summary>
    public void Open(string? phone = null, string? prefilledText = null)
    {
        PendingUrl = BuildUrl(phone, prefilledText);
        MainThread.BeginInvokeOnMainThread(() => NavigateToWhatsAppRequested?.Invoke());
    }

    /// <summary>Called from the /whatsapp page with the measured container bounds.</summary>
    public void OpenAt(string? phone, string? prefilledText, double left, double top, double width, double height)
    {
        var url = phone is not null ? BuildUrl(phone, prefilledText) : (PendingUrl ?? "https://web.whatsapp.com");
        PendingUrl = null;
        MainThread.BeginInvokeOnMainThread(() =>
            OpenRequested?.Invoke(url, left, top, width, height));
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
