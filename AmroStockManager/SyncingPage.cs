namespace AmroStockManager;

public class SyncingPage : ContentPage
{
    public SyncingPage()
    {
        BackgroundColor = Color.FromArgb("#1565C0");
        Content = new VerticalStackLayout
        {
            VerticalOptions  = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Spacing  = 14,
            Padding  = new Thickness(32),
            Children =
            {
                new ActivityIndicator
                {
                    IsRunning         = true,
                    Color             = Colors.White,
                    HeightRequest     = 40,
                    WidthRequest      = 40,
                    HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text                  = "A sincronizar dados…",
                    TextColor             = Colors.White,
                    FontSize              = 15,
                    FontAttributes        = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                new Label
                {
                    Text                  = "Por favor aguarde.",
                    TextColor             = Color.FromArgb("#90CAF9"),
                    FontSize              = 12,
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };
    }
}
