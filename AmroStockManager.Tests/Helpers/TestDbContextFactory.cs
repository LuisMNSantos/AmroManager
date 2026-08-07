using AmroStockManager.Data;
using AmroStockManager.Data.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AmroStockManager.Tests.Helpers;

/// <summary>
/// Creates an isolated in-memory SQLite database for each test class.
/// Keeps the connection open so the in-memory DB persists for the lifetime of the factory.
/// </summary>
public sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public AppDbContext CreateDbContext() => new(_options);

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());

    /// <summary>Seeds a GeneralItem and returns its Id.</summary>
    public int SeedGeneralItem(string name, int totalQuantity = 1)
    {
        using var db = CreateDbContext();
        var item = new GeneralItem { Name = name, TotalQuantity = totalQuantity };
        db.GeneralItems.Add(item);
        db.SaveChanges();
        return item.Id;
    }

    /// <summary>Seeds a Resident and returns the entity.</summary>
    public Resident SeedResident(string name, string roomNumber, string? phone = null, bool isCollaborator = false)
    {
        using var db = CreateDbContext();
        var resident = new Resident { Name = name, RoomNumber = roomNumber, PhoneNumber = phone, IsCollaborator = isCollaborator };
        db.Residents.Add(resident);
        db.SaveChanges();
        return resident;
    }

    public void Dispose() => _connection.Dispose();
}
