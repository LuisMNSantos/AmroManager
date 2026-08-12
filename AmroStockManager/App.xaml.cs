using AmroStockManager.Services;
using Microsoft.UI.Windowing;

namespace AmroStockManager;

public partial class App : Application
{
    public App(SupabaseRealtimeService realtime)
    {
        InitializeComponent();
        _ = realtime.StartAsync();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage()) { Title = "AmroStockManager" };
    }
}
