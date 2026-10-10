using Microsoft.Maui.Storage;

namespace AmroStockManager;

internal static class AppEnvironment
{
    private const string PrefKey = "db_environment";

    public static bool IsDev
    {
        get => Preferences.Default.Get(PrefKey, defaultValue: true);
        set => Preferences.Default.Set(PrefKey, value);
    }

    public static string SupabaseUrl => IsDev ? AppSecrets.DevSupabaseUrl : AppSecrets.ProdSupabaseUrl;
    public static string SupabaseKey => IsDev ? AppSecrets.DevSupabaseKey : AppSecrets.ProdSupabaseKey;
    public static string Label       => IsDev ? "DEV" : "PROD";
}
