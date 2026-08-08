using System.Diagnostics;
using AmroStockManager.Data;
using Microsoft.EntityFrameworkCore;

namespace AmroStockManager.Services;

public class BackupService(IDbContextFactory<AppDbContext> dbFactory)
{
    public string BackupFolder { get; } = ResolveBackupFolder();
    public bool IsOneDrive => BackupFolder.Contains("OneDrive", StringComparison.OrdinalIgnoreCase);

    private static string ResolveBackupFolder()
    {
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrEmpty(oneDrive) && Directory.Exists(oneDrive))
            return Path.Combine(oneDrive, "AmroStock_Backups");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AmroStock_Backups");
    }

    public DateTime? LastBackupTime
    {
        get
        {
            if (!Directory.Exists(BackupFolder)) return null;
            var files = Directory.GetFiles(BackupFolder, "stock_*.db");
            return files.Length == 0 ? null : files.Max(File.GetLastWriteTime);
        }
    }

    public int BackupCount => Directory.Exists(BackupFolder)
        ? Directory.GetFiles(BackupFolder, "stock_*.db").Length
        : 0;

    public async Task<string> RunBackupAsync(int keepCount = 7)
    {
        Directory.CreateDirectory(BackupFolder);

        var destPath = Path.Combine(BackupFolder, $"stock_{DateTime.Now:yyyyMMdd_HHmmss}.db");

        // Remove if it already exists (e.g. two rapid startups within the same second)
        if (File.Exists(destPath)) File.Delete(destPath);

        // VACUUM INTO produces a clean, consistent copy even while the DB is open
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlAsync($"VACUUM INTO {destPath}");

        // Purge oldest copies beyond the keep limit
        foreach (var old in Directory.GetFiles(BackupFolder, "stock_*.db")
                     .OrderByDescending(f => f)
                     .Skip(keepCount))
        {
            File.Delete(old);
        }

        return destPath;
    }

    public void OpenBackupFolder()
    {
        Directory.CreateDirectory(BackupFolder);
        Process.Start(new ProcessStartInfo
        {
            FileName  = BackupFolder,
            UseShellExecute = true
        });
    }
}
