using DAL;
using DAL.EF;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Entities;
using Models.Enum;

namespace Tests;

/// <summary>
/// Shared SQLite in-memory database (named, shared-cache) seeded with test data.
/// A sentinel connection is kept open for the fixture's entire lifetime so the
/// in-memory database survives between EF Core operations (EF Core opens/closes
/// its own connections per operation by default, which would otherwise wipe the DB).
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    // Named in-memory SQLite with shared cache: multiple independent connections see the same data.
    public string ConnectionString { get; } =
        $"Data Source=repo_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

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
        // Keep this connection open for the entire fixture lifetime.
        // Without it, each EF Core operation closes its internal connection,
        // causing the in-memory database to be wiped between EnsureCreated() and SaveChangesAsync().
        _keepAlive = new SqliteConnection(ConnectionString);
        await _keepAlive.OpenAsync();

        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        ctx.Users.AddRange(
            new UserEntity { Id = 1, Username = "alice", Email = "alice@example.com", Password = "pass1", Created_At = DateTime.UtcNow },
            new UserEntity { Id = 2, Username = "bob",   Email = "bob@example.com",   Password = "pass2", Created_At = DateTime.UtcNow }
        );
        ctx.Tenants.AddRange(
            new TenantEntity { Id = 1, Name = "Acme" },
            new TenantEntity { Id = 2, Name = "Globex" }
        );
        ctx.UserTenants.AddRange(
            new UserTenantsEntity { UserId = 1, TenantId = 1, Status = TenantUserStatus.Pending },
            new UserTenantsEntity { UserId = 1, TenantId = 2, Status = TenantUserStatus.Unaccpeted },
            new UserTenantsEntity { UserId = 2, TenantId = 1, Status = TenantUserStatus.Pending }
        );
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_keepAlive != null)
            await _keepAlive.DisposeAsync();
    }
}
