namespace AmroStockManager.Services;

public sealed class ConnectivityService : IDisposable
{
    private readonly IConnectivity _connectivity;

    public event Action? Changed;

    public bool IsOnline => _connectivity.NetworkAccess == NetworkAccess.Internet;

    public ConnectivityService(IConnectivity connectivity)
    {
        _connectivity = connectivity;
        _connectivity.ConnectivityChanged += OnChanged;
    }

    private void OnChanged(object? sender, ConnectivityChangedEventArgs e) => Changed?.Invoke();

    public void Dispose() => _connectivity.ConnectivityChanged -= OnChanged;
}
