using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using AmroStockManager.Services;

namespace AmroStockManager;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices();

        builder.Services.AddSingleton<ISupabaseClient, SupabaseClient>();
        builder.Services.AddSingleton<CacheService>();
        builder.Services.AddSingleton<ConnectivityService>(sp =>
            new ConnectivityService(Connectivity.Current));
        builder.Services.AddSingleton<SupabaseRealtimeService>();

        builder.Services.AddSingleton<ProductService>();
        builder.Services.AddSingleton<StockService>();
        builder.Services.AddSingleton<GeneralItemService>();
        builder.Services.AddSingleton<ReservationService>();
        builder.Services.AddSingleton<DeliveryService>();
        builder.Services.AddSingleton<MaintenanceService>();
        builder.Services.AddSingleton<ResidentService>();
        builder.Services.AddSingleton<RoomService>();
        builder.Services.AddSingleton<DistributionService>();
        builder.Services.AddSingleton<BisLoanService>();
        builder.Services.AddSingleton<PendingRegistrationService>();
        builder.Services.AddSingleton<AuditLogService>();
        builder.Services.AddSingleton<VisitService>();
        builder.Services.AddSingleton<RenewerKitService>();
        builder.Services.AddSingleton<ReimbursementService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
