using AmroStockManager.Services;
using Microsoft.UI.Windowing;

namespace AmroStockManager;

public partial class App : Application
{
    private readonly WhatsAppService _whatsApp;

    public App(SupabaseRealtimeService realtime, WhatsAppService whatsApp)
    {
        InitializeComponent();
        _whatsApp = whatsApp;
        _ = realtime.StartAsync();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage(_whatsApp)) { Title = "AmroManager" };
    }
}
