using DAL;
using DAL.EF;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>
/// Spins up a named in-memory SQLite database seeded with 100 000 users,
/// 20 tenants, and one UserTenants row per user.
/// Schema is created via EF Core EnsureCreated(); data is inserted with raw
/// SQL inside a single transaction so the fixture initialises in under a second.
/// </summary>
public class LargeDatasetFixture : IAsyncLifetime
{
    public const int UserCount   = 100_000;
    public const int TenantCount = 20;

    public string ConnectionString { get; } =
        $"Data Source=large_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    private SqliteConnection? _keepAlive;

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    public IDbConnectionFactory CreateConnectionFactory() =>
        new SqliteConnectionFactory(ConnectionString);

    public DAL.RawDapper.IDbConnectionFactory CreateRawDapperConnectionFactory() =>
        new DAL.RawDapper.SqliteConnectionFactory(ConnectionString);

    public async Task InitializeAsync()
    {
        _keepAlive = new SqliteConnection(ConnectionString);
        await _keepAlive.OpenAsync();

        // Create schema via EF Core
        await using (var ctx = CreateContext())
            await ctx.Database.EnsureCreatedAsync();

        // Bulk-seed via raw SQL in one transaction — orders of magnitude faster
        // than going through EF Core's change tracker for 100k rows.
        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync(
            "INSERT INTO Tenants (Id, Name) VALUES (@Id, @Name)",
            Enumerable.Range(1, TenantCount).Select(i => new { Id = i, Name = $"Tenant{i}" }),
            tx);

        await conn.ExecuteAsync(
            "INSERT INTO Users (Id, Username, Email, Password, Created_At) VALUES (@Id, @Username, @Email, @Password, @Created_At)",
            Enumerable.Range(1, UserCount).Select(i => new
            {
                Id         = i,
                Username   = $"user{i}",
                Email      = $"user{i}@example.com",
                Password   = $"pass{i}",
                Created_At = DateTime.UtcNow.ToString("O")
            }),
            tx);

        // Each user belongs to one tenant: userId % TenantCount + 1
        await conn.ExecuteAsync(
            "INSERT INTO UserTenants (UserId, TenantId, Status) VALUES (@UserId, @TenantId, @Status)",
            Enumerable.Range(1, UserCount).Select(i => new
            {
                UserId   = i,
                TenantId = i % TenantCount + 1,
                Status   = 0
            }),
            tx);

        await tx.CommitAsync();
    }

    public async Task DisposeAsync()
    {
        if (_keepAlive != null)
            await _keepAlive.DisposeAsync();
    }
}
