using DAL.EF;
using DAL.Repositories;
using Models;
using Models.Entities;
using Models.Enum;
using RawDapperRepo = DAL.RawDapper.Repositories.UserRepository;

namespace Tests;

public class UserRepositoryComparisonTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private async Task<T> WithEf<T>(Func<EfCoreUserRepository, Task<T>> action)
    {
        await using var ctx = fixture.CreateContext();
        return await action(new EfCoreUserRepository(ctx));
    }

    private DapperHelperUserRepository Dapper()   => new(fixture.CreateConnectionFactory());
    private RawDapperRepo              RawDapper() => new(fixture.CreateRawDapperConnectionFactory());

    // ── GetAllAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_AllThreeReturnSameUsers()
    {
        var ef     = (await WithEf(r => r.GetAllAsync())).OrderBy(u => u.Id).ToList();
        var dapper = (await Dapper().GetAllAsync()).OrderBy(u => u.Id).ToList();
        var raw    = (await RawDapper().GetAllAsync()).OrderBy(u => u.Id).ToList();

        Assert.Equal(ef.Count, dapper.Count);
        Assert.Equal(ef.Count, raw.Count);
        for (var i = 0; i < ef.Count; i++)
        {
            AssertUsersEqual(ef[i], dapper[i]);
            AssertUsersEqual(ef[i], raw[i]);
        }
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
    public async Task GetByIdAsync_AllThreeReturnSameUser(int id)
    {
        var ef     = await WithEf(r => r.GetByIdAsync(id));
        var dapper = await Dapper().GetByIdAsync(id);
        var raw    = await RawDapper().GetByIdAsync(id);

        Assert.NotNull(ef);
        Assert.NotNull(dapper);
        Assert.NotNull(raw);
        AssertUsersEqual(ef, dapper);
        AssertUsersEqual(ef, raw);
    }

    [Fact]
    public async Task GetByIdAsync_AllThreeReturnNullForMissingId()
    {
        var ef     = await WithEf(r => r.GetByIdAsync(999));
        var dapper = await Dapper().GetByIdAsync(999);
        var raw    = await RawDapper().GetByIdAsync(999);

        Assert.Null(ef);
        Assert.Null(dapper);
        Assert.Null(raw);
    }

    // ── GetByCredentialAsync ─────────────────────────────────────────────────

    [Theory]
    [InlineData("alice@example.com", "pass1", 1)]
    [InlineData("bob@example.com",   "pass2", 2)]
    public async Task GetByCredentialAsync_AllThreeReturnCorrectUser(string email, string password, int expectedId)
    {
        var ef     = await WithEf(r => r.GetByCredentialAsync(email, password));
        var dapper = await Dapper().GetByCredentialAsync(email, password);
        var raw    = await RawDapper().GetByCredentialAsync(email, password);

        Assert.NotNull(ef);
        Assert.NotNull(dapper);
        Assert.NotNull(raw);
        Assert.Equal(expectedId, ef.Id);
        Assert.Equal(expectedId, dapper.Id);
        Assert.Equal(expectedId, raw.Id);
        AssertUsersEqual(ef, dapper);
        AssertUsersEqual(ef, raw);
    }

    [Theory]
    [InlineData("alice@example.com", "wrongpass")]
    [InlineData("nobody@example.com", "pass1")]
    public async Task GetByCredentialAsync_AllThreeReturnNullForBadCredentials(string email, string password)
    {
        var ef     = await WithEf(r => r.GetByCredentialAsync(email, password));
        var dapper = await Dapper().GetByCredentialAsync(email, password);
        var raw    = await RawDapper().GetByCredentialAsync(email, password);

        Assert.Null(ef);
        Assert.Null(dapper);
        Assert.Null(raw);
    }

    // ── GetUserWithTenants ───────────────────────────────────────────────────

    [Fact]
    public async Task GetUserWithTenants_AllThreeReturnSameDto_ForUserWithMultipleTenants()
    {
        var ef     = await WithEf(r => r.GetUserWithTenants(1));
        var dapper = await Dapper().GetUserWithTenants(1);
        var raw    = await RawDapper().GetUserWithTenants(1);

        Assert.NotNull(ef);
        Assert.NotNull(dapper);
        Assert.NotNull(raw);
        Assert.Equal(ef.Id,       dapper.Id);
        Assert.Equal(ef.Username, dapper.Username);
        Assert.Equal(ef.Email,    dapper.Email);
        Assert.Equal(ef.Id,       raw.Id);
        Assert.Equal(ef.Username, raw.Username);
        Assert.Equal(ef.Email,    raw.Email);

        var efTenants     = ef.Tenants.OrderBy(t => t.Id).ToList();
        var dapperTenants = dapper.Tenants.OrderBy(t => t.Id).ToList();
        var rawTenants    = raw.Tenants.OrderBy(t => t.Id).ToList();

        Assert.Equal(efTenants.Count, dapperTenants.Count);
        Assert.Equal(efTenants.Count, rawTenants.Count);
        for (var i = 0; i < efTenants.Count; i++)
        {
            AssertTenantsEqual(efTenants[i], dapperTenants[i]);
            AssertTenantsEqual(efTenants[i], rawTenants[i]);
        }
    }

    [Fact]
    public async Task GetUserWithTenants_AllThreeReturnSameDto_ForUserWithSingleTenant()
    {
        var ef     = await WithEf(r => r.GetUserWithTenants(2));
        var dapper = await Dapper().GetUserWithTenants(2);
        var raw    = await RawDapper().GetUserWithTenants(2);

        Assert.NotNull(ef);
        Assert.NotNull(dapper);
        Assert.NotNull(raw);
        Assert.Equal(ef.Id, dapper.Id);
        Assert.Equal(ef.Id, raw.Id);

        var efTenants     = ef.Tenants.OrderBy(t => t.Id).ToList();
        var dapperTenants = dapper.Tenants.OrderBy(t => t.Id).ToList();
        var rawTenants    = raw.Tenants.OrderBy(t => t.Id).ToList();

        Assert.Single(efTenants);
        Assert.Single(dapperTenants);
        Assert.Single(rawTenants);
        AssertTenantsEqual(efTenants[0], dapperTenants[0]);
        AssertTenantsEqual(efTenants[0], rawTenants[0]);
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
    public async Task GetUserWithTenants_AllThreeReturnNullForMissingUser()
    {
        var ef     = await WithEf(r => r.GetUserWithTenants(999));
        var dapper = await Dapper().GetUserWithTenants(999);
        var raw    = await RawDapper().GetUserWithTenants(999);

        Assert.Null(ef);
        Assert.Null(dapper);
        Assert.Null(raw);
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
