using System.Diagnostics;
using DAL.EF;
using DAL.Repositories;
using RawDapperRepo = DAL.RawDapper.Repositories.UserRepository;

namespace Tests;

/// <summary>
/// Measures wall-clock time for EF Core vs DapperHelper vs Raw Dapper across the shared
/// repository interface.  Tests do NOT assert a winner — SQLite in-memory timings are
/// noisy and machine-dependent — but they do assert that no ORM exceeds a generous
/// per-operation ceiling, and they print a side-by-side summary so differences are
/// visible in the test output.
///
/// Methodology:
///   - Warm-up iterations run first and are excluded from timing.
///   - Timed iterations are run with a fresh context / connection each time so
///     EF Core's change-tracker and Dapper's connection-open overhead are both included.
///   - The "per-call" figure is total elapsed / timed iterations.
/// </summary>
public class PerformanceComparisonTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private const int WarmUpIterations = 5;
    private const int TimedIterations  = 50;

    private static readonly TimeSpan MaxPerCall = TimeSpan.FromMilliseconds(100);

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<TimeSpan> MeasureEf(Func<EfCoreUserRepository, Task> action)
    {
        for (var i = 0; i < WarmUpIterations; i++)
        {
            await using var ctx = fixture.CreateContext();
            await action(new EfCoreUserRepository(ctx));
        }
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < TimedIterations; i++)
        {
            await using var ctx = fixture.CreateContext();
            await action(new EfCoreUserRepository(ctx));
        }
        sw.Stop();
        return sw.Elapsed;
    }

    private async Task<TimeSpan> MeasureDapper(Func<DapperHelperUserRepository, Task> action)
    {
        for (var i = 0; i < WarmUpIterations; i++)
            await action(new DapperHelperUserRepository(fixture.CreateConnectionFactory()));
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < TimedIterations; i++)
            await action(new DapperHelperUserRepository(fixture.CreateConnectionFactory()));
        sw.Stop();
        return sw.Elapsed;
    }

    private async Task<TimeSpan> MeasureRawDapper(Func<RawDapperRepo, Task> action)
    {
        for (var i = 0; i < WarmUpIterations; i++)
            await action(new RawDapperRepo(fixture.CreateRawDapperConnectionFactory()));
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < TimedIterations; i++)
            await action(new RawDapperRepo(fixture.CreateRawDapperConnectionFactory()));
        sw.Stop();
        return sw.Elapsed;
    }

    private static void Report(string scenario, TimeSpan ef, TimeSpan dapper, TimeSpan raw)
    {
        var efMs     = ef.TotalMilliseconds     / TimedIterations;
        var dapperMs = dapper.TotalMilliseconds / TimedIterations;
        var rawMs    = raw.TotalMilliseconds    / TimedIterations;

        Console.WriteLine($"""

            ┌─ {scenario}
            │  EF Core    : {ef.TotalMilliseconds,8:F1} ms total  ({efMs,6:F3} ms/call)
            │  Dapper     : {dapper.TotalMilliseconds,8:F1} ms total  ({dapperMs,6:F3} ms/call)
            │  Raw Dapper : {raw.TotalMilliseconds,8:F1} ms total  ({rawMs,6:F3} ms/call)
            │  EF/Dapper  : {efMs/dapperMs,6:F2}x  ({(efMs/dapperMs > 1 ? "EF slower" : "EF faster")})
            │  EF/Raw     : {efMs/rawMs,6:F2}x  ({(efMs/rawMs > 1 ? "EF slower" : "EF faster")})
            └─────────────────────────────────────────────────────────
            """);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Performance_GetAllAsync()
    {
        var ef     = await MeasureEf        (r => r.GetAllAsync().AsTask());
        var dapper = await MeasureDapper    (r => r.GetAllAsync().AsTask());
        var raw    = await MeasureRawDapper (r => r.GetAllAsync().AsTask());

        Report("GetAllAsync (2 rows)", ef, dapper, raw);

        Assert.True(ef     / TimedIterations < MaxPerCall, $"EF Core GetAllAsync too slow: {ef/TimedIterations}");
        Assert.True(dapper / TimedIterations < MaxPerCall, $"Dapper GetAllAsync too slow: {dapper/TimedIterations}");
        Assert.True(raw    / TimedIterations < MaxPerCall, $"Raw Dapper GetAllAsync too slow: {raw/TimedIterations}");
    }

    [Fact]
    public async Task Performance_GetByIdAsync()
    {
        var ef     = await MeasureEf        (r => r.GetByIdAsync(1)!);
        var dapper = await MeasureDapper    (r => r.GetByIdAsync(1)!);
        var raw    = await MeasureRawDapper (r => r.GetByIdAsync(1)!);

        Report("GetByIdAsync (single row lookup)", ef, dapper, raw);

        Assert.True(ef     / TimedIterations < MaxPerCall, $"EF Core GetByIdAsync too slow: {ef/TimedIterations}");
        Assert.True(dapper / TimedIterations < MaxPerCall, $"Dapper GetByIdAsync too slow: {dapper/TimedIterations}");
        Assert.True(raw    / TimedIterations < MaxPerCall, $"Raw Dapper GetByIdAsync too slow: {raw/TimedIterations}");
    }

    [Fact]
    public async Task Performance_GetByCredentialAsync()
    {
        var ef     = await MeasureEf        (r => r.GetByCredentialAsync("alice@example.com", "pass1")!);
        var dapper = await MeasureDapper    (r => r.GetByCredentialAsync("alice@example.com", "pass1")!);
        var raw    = await MeasureRawDapper (r => r.GetByCredentialAsync("alice@example.com", "pass1")!);

        Report("GetByCredentialAsync (email+password filter)", ef, dapper, raw);

        Assert.True(ef     / TimedIterations < MaxPerCall, $"EF Core GetByCredentialAsync too slow: {ef/TimedIterations}");
        Assert.True(dapper / TimedIterations < MaxPerCall, $"Dapper GetByCredentialAsync too slow: {dapper/TimedIterations}");
        Assert.True(raw    / TimedIterations < MaxPerCall, $"Raw Dapper GetByCredentialAsync too slow: {raw/TimedIterations}");
    }

    [Fact]
    public async Task Performance_GetUserWithTenants()
    {
        var ef     = await MeasureEf        (r => r.GetUserWithTenants(1)!);
        var dapper = await MeasureDapper    (r => r.GetUserWithTenants(1)!);
        var raw    = await MeasureRawDapper (r => r.GetUserWithTenants(1)!);

        Report("GetUserWithTenants (3-table join, 2 tenants)", ef, dapper, raw);

        Assert.True(ef     / TimedIterations < MaxPerCall, $"EF Core GetUserWithTenants too slow: {ef/TimedIterations}");
        Assert.True(dapper / TimedIterations < MaxPerCall, $"Dapper GetUserWithTenants too slow: {dapper/TimedIterations}");
        Assert.True(raw    / TimedIterations < MaxPerCall, $"Raw Dapper GetUserWithTenants too slow: {raw/TimedIterations}");
    }
}

file static class EnumerableExtensions
{
    public static Task AsTask<T>(this Task<IEnumerable<T>> task) => task;
}
