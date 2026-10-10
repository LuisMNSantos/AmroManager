namespace AmroStockManager;

// Thin wrapper — on the dev branch this class adds a runtime Prod/Dev toggle.
// On master it simply forwards to AppSecrets (single production environment).
internal static class AppEnvironment
{
    public static string SupabaseUrl => AppSecrets.SupabaseUrl;
    public static string SupabaseKey => AppSecrets.SupabaseKey;
}
