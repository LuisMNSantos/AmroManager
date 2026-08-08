using AmroStockManager.Services;
using Microsoft.UI.Windowing;

namespace AmroStockManager;

public partial class App : Application
{
    private Window? _mainWindow;
    private bool _closingAfterSync;

    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _mainWindow = new Window(new MainPage()) { Title = "AmroStockManager" };

        _mainWindow.Created += (_, _) =>
        {
            if (_mainWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window winUiWindow)
                winUiWindow.AppWindow.Closing += OnAppWindowClosing;
        };

        return _mainWindow;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closingAfterSync) return;

        var syncSvc = IPlatformApplication.Current?.Services.GetService<SyncService>();
        if (syncSvc?.IsConfigured != true) return;

        args.Cancel = true;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var syncWindow = new Window(new SyncingPage())
            {
                Title         = "AmroStockManager",
                Width         = 320,
                Height        = 150,
                MinimumWidth  = 320,
                MinimumHeight = 150,
                MaximumWidth  = 320,
                MaximumHeight = 150
            };
            OpenWindow(syncWindow);

            _ = Task.Run(async () =>
            {
                try   { await syncSvc.SyncAsync(); }
                catch { /* silent — we're closing anyway */ }
                finally
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        CloseWindow(syncWindow);
                        _closingAfterSync = true;
                        CloseWindow(_mainWindow!);
                    });
                }
            });
        });
    }
}
