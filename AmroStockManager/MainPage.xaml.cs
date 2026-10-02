using AmroStockManager.Services;

namespace AmroStockManager;

public partial class MainPage : ContentPage
{
    public MainPage(WhatsAppService whatsApp)
    {
        InitializeComponent();
        whatsApp.LoadRequested  += OnLoadWhatsApp;
        whatsApp.ShowRequested  += OnShowWhatsApp;
        whatsApp.CloseRequested += OnCloseWhatsApp;
    }

    private void OnLoadWhatsApp(string url, double left, double top, double width, double height)
    {
        WhatsAppWebView.Source = new UrlWebViewSource { Url = url };
        AbsoluteLayout.SetLayoutBounds(WhatsAppWebView, new Rect(left, top, width, height));
        WhatsAppWebView.IsVisible = true;
    }

    private void OnShowWhatsApp(double left, double top, double width, double height)
    {
        AbsoluteLayout.SetLayoutBounds(WhatsAppWebView, new Rect(left, top, width, height));
        WhatsAppWebView.IsVisible = true;
    }

    private void OnCloseWhatsApp()
    {
        WhatsAppWebView.IsVisible = false;
    }
}
