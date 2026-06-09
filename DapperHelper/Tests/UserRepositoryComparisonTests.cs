using Models;
using Models.Entities;
using Models.Enum;
using DapperUserRepository = DAL.Repositories.UserRepository;
using EfUserRepository = DAL.EF.UserRepository;

namespace Tests;

public class UserRepositoryComparisonTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    // Each call gets a fresh EF context so the change tracker never bleeds between assertions.
    private async Task<T> WithEf<T>(Func<EfUserRepository, Task<T>> action)
    {
        await using var ctx = fixture.CreateContext();
        return await action(new EfUserRepository(ctx));
    }

    private DapperUserRepository Dapper() => new(fixture.CreateConnectionFactory());

    // ── GetAllAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_BothReturnSameUsers()
    {
        var ef     = (await WithEf(r => r.GetAllAsync())).OrderBy(u => u.Id).ToList();
        var dapper = (await Dapper().GetAllAsync()).OrderBy(u => u.Id).ToList();

        Assert.Equal(ef.Count, dapper.Count);
        for (var i = 0; i < ef.Count; i++)
            AssertUsersEqual(ef[i], dapper[i]);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsBothSeededUsers()
    {
        var users = (await WithEf(r => r.GetAllAsync())).OrderBy(u => u.Id).ToList();

        Assert.Equal(2, users.Count);
        Assert.Equal("alice", users[0].Username);
        Assert.Equal("bob",   users[1].Username);
    }

    // ── GetByIdAsync ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GetByIdAsync_BothReturnSameUser(int id)
    {
        var ef     = await WithEf(r => r.GetByIdAsync(id));
        var dapper = await Dapper().GetByIdAsync(id);

        Assert.NotNull(ef);
        Assert.NotNull(dapper);
        AssertUsersEqual(ef, dapper);
    }

    [Fact]
    public async Task GetByIdAsync_BothReturnNullForMissingId()
    {
        var ef     = await WithEf(r => r.GetByIdAsync(999));
        var dapper = await Dapper().GetByIdAsync(999);

        Assert.Null(ef);
        Assert.Null(dapper);
    }

    // ── GetByCredentialAsync ─────────────────────────────────────────────────

    [Theory]
    [InlineData("alice@example.com", "pass1", 1)]
    [InlineData("bob@example.com",   "pass2", 2)]
    public async Task GetByCredentialAsync_BothReturnCorrectUser(string email, string password, int expectedId)
    {
        var ef     = await WithEf(r => r.GetByCredentialAsync(email, password));
        var dapper = await Dapper().GetByCredentialAsync(email, password);

        Assert.NotNull(ef);
        Assert.NotNull(dapper);
        Assert.Equal(expectedId, ef.Id);
        Assert.Equal(expectedId, dapper.Id);
        AssertUsersEqual(ef, dapper);
    }

    [Theory]
    [InlineData("alice@example.com", "wrongpass")]
    [InlineData("nobody@example.com", "pass1")]
    public async Task GetByCredentialAsync_BothReturnNullForBadCredentials(string email, string password)
    {
        var ef     = await WithEf(r => r.GetByCredentialAsync(email, password));
        var dapper = await Dapper().GetByCredentialAsync(email, password);

        Assert.Null(ef);
        Assert.Null(dapper);
    }

    // ── GetUserWithTenants ───────────────────────────────────────────────────

    [Fact]
    public async Task GetUserWithTenants_BothReturnSameDto_ForUserWithMultipleTenants()
    {
        var ef     = await WithEf(r => r.GetUserWithTenants(1));
        var dapper = await Dapper().GetUserWithTenants(1);

        Assert.NotNull(ef);
        Assert.NotNull(dapper);
        Assert.Equal(ef.Id,       dapper.Id);
        Assert.Equal(ef.Username, dapper.Username);
        Assert.Equal(ef.Email,    dapper.Email);

        var efTenants     = ef.Tenants.OrderBy(t => t.Id).ToList();
        var dapperTenants = dapper.Tenants.OrderBy(t => t.Id).ToList();

        Assert.Equal(efTenants.Count, dapperTenants.Count);
        for (var i = 0; i < efTenants.Count; i++)
            AssertTenantsEqual(efTenants[i], dapperTenants[i]);
    }

    [Fact]
    public async Task GetUserWithTenants_BothReturnSameDto_ForUserWithSingleTenant()
    {
        var ef     = await WithEf(r => r.GetUserWithTenants(2));
        var dapper = await Dapper().GetUserWithTenants(2);

        Assert.NotNull(ef);
        Assert.NotNull(dapper);
        Assert.Equal(ef.Id, dapper.Id);

        var efTenants     = ef.Tenants.OrderBy(t => t.Id).ToList();
        var dapperTenants = dapper.Tenants.OrderBy(t => t.Id).ToList();

        Assert.Single(efTenants);
        Assert.Single(dapperTenants);
        AssertTenantsEqual(efTenants[0], dapperTenants[0]);
    }

    [Fact]
    public async Task GetUserWithTenants_AliceTenantNames_AreCorrect()
    {
        var result = await WithEf(r => r.GetUserWithTenants(1));

        Assert.NotNull(result);
        var names = result.Tenants.Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Equal(["Acme", "Globex"], names);
    }

    [Fact]
    public async Task GetUserWithTenants_AliceTenantStatuses_AreCorrect()
    {
        var result = await WithEf(r => r.GetUserWithTenants(1));

        Assert.NotNull(result);
        var byTenant = result.Tenants.ToDictionary(t => t.Name);
        Assert.Equal(TenantUserStatus.Pending,    byTenant["Acme"].Status);
        Assert.Equal(TenantUserStatus.Unaccpeted, byTenant["Globex"].Status);
    }

    [Fact]
    public async Task GetUserWithTenants_BothReturnNullForMissingUser()
    {
        var ef     = await WithEf(r => r.GetUserWithTenants(999));
        var dapper = await Dapper().GetUserWithTenants(999);

        Assert.Null(ef);
        Assert.Null(dapper);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void AssertUsersEqual(UserEntity a, UserEntity b)
    {
        Assert.Equal(a.Id,       b.Id);
        Assert.Equal(a.Username, b.Username);
        Assert.Equal(a.Email,    b.Email);
        Assert.Equal(a.Password, b.Password);
    }

    private static void AssertTenantsEqual(CustomTenantDto a, CustomTenantDto b)
    {
        Assert.Equal(a.Id,     b.Id);
        Assert.Equal(a.Name,   b.Name);
        Assert.Equal(a.Status, b.Status);
    }
}
