namespace AmroStockManager;

// AppSecrets always defines DevSupabaseUrl/Key + ProdSupabaseUrl/Key so the
// same AppSecrets.cs works on both branches without manual edits on checkout.
// Only this file differs between branches:
//   master → always prod
//   dev    → runtime toggle via Preferences (defaults to dev)
internal static class AppEnvironment
{
    public static string SupabaseUrl => AppSecrets.ProdSupabaseUrl;
    public static string SupabaseKey => AppSecrets.ProdSupabaseKey;
}
