using System.Diagnostics;
using DAL.EF;
using DAL.Repositories;
using RawDapperRepo = DAL.RawDapper.Repositories.UserRepository;

namespace Tests;

/// <summary>
/// Performance comparison on a 100 000-row dataset.
///
/// Iteration counts are tuned per-method:
///   - GetAllAsync       : 3 timed runs — loads the full table each time, very heavy
///   - Single-row ops    : 20 timed runs — fast I/O, enough for stable averages
///
/// Assertions only check that no ORM exceeds a generous ceiling so tests
/// stay green on slow CI machines.  Read the printed report for the actual
/// three-way comparison.
/// </summary>
public class PerformanceLargeDatasetTests(LargeDatasetFixture fixture) : IClassFixture<LargeDatasetFixture>
{
    private const int WarmUp         = 2;
    private const int TimedBulk      = 3;
    private const int TimedSingleRow = 20;

    private static readonly TimeSpan CeilBulk      = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CeilSingleRow = TimeSpan.FromMilliseconds(200);

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<TimeSpan> MeasureEf(Func<EfCoreUserRepository, Task> action, int warmUp, int timed)
    {
        for (var i = 0; i < warmUp; i++)
        {
            await using var ctx = fixture.CreateContext();
            await action(new EfCoreUserRepository(ctx));
        }
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < timed; i++)
        {
            await using var ctx = fixture.CreateContext();
            await action(new EfCoreUserRepository(ctx));
        }
        sw.Stop();
        return sw.Elapsed;
    }

    private async Task<TimeSpan> MeasureDapper(Func<DapperHelperUserRepository, Task> action, int warmUp, int timed)
    {
        for (var i = 0; i < warmUp; i++)
            await action(new DapperHelperUserRepository(fixture.CreateConnectionFactory()));
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < timed; i++)
            await action(new DapperHelperUserRepository(fixture.CreateConnectionFactory()));
        sw.Stop();
        return sw.Elapsed;
    }

    private async Task<TimeSpan> MeasureRawDapper(Func<RawDapperRepo, Task> action, int warmUp, int timed)
    {
        for (var i = 0; i < warmUp; i++)
            await action(new RawDapperRepo(fixture.CreateRawDapperConnectionFactory()));
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < timed; i++)
            await action(new RawDapperRepo(fixture.CreateRawDapperConnectionFactory()));
        sw.Stop();
        return sw.Elapsed;
    }

    private static void Report(string scenario, int iterations, TimeSpan ef, TimeSpan dapper, TimeSpan raw)
    {
        var efMs     = ef.TotalMilliseconds     / iterations;
        var dapperMs = dapper.TotalMilliseconds / iterations;
        var rawMs    = raw.TotalMilliseconds    / iterations;

        Console.WriteLine($"""

            ┌─ [100k rows] {scenario}  ({iterations} iterations)
            │  EF Core    : {ef.TotalMilliseconds,9:F1} ms total  ({efMs,8:F2} ms/call)
            │  Dapper     : {dapper.TotalMilliseconds,9:F1} ms total  ({dapperMs,8:F2} ms/call)
            │  Raw Dapper : {raw.TotalMilliseconds,9:F1} ms total  ({rawMs,8:F2} ms/call)
            │  EF/Dapper  : {efMs/dapperMs,6:F2}x  ({(efMs/dapperMs > 1 ? "EF slower" : "EF faster")})
            │  EF/Raw     : {efMs/rawMs,6:F2}x  ({(efMs/rawMs > 1 ? "EF slower" : "EF faster")})
            └─────────────────────────────────────────────────────────
            """);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Performance_GetAllAsync_LargeDataset()
    {
        var ef     = await MeasureEf        (r => r.GetAllAsync().AsTask(), WarmUp, TimedBulk);
        var dapper = await MeasureDapper    (r => r.GetAllAsync().AsTask(), WarmUp, TimedBulk);
        var raw    = await MeasureRawDapper (r => r.GetAllAsync().AsTask(), WarmUp, TimedBulk);

        Report($"GetAllAsync ({LargeDatasetFixture.UserCount:N0} users)", TimedBulk, ef, dapper, raw);

        Assert.True(ef     / TimedBulk <= CeilBulk, $"EF Core GetAllAsync too slow: {ef/TimedBulk}");
        Assert.True(dapper / TimedBulk <= CeilBulk, $"Dapper  GetAllAsync too slow: {dapper/TimedBulk}");
        Assert.True(raw    / TimedBulk <= CeilBulk, $"Raw Dapper GetAllAsync too slow: {raw/TimedBulk}");
    }

    [Fact]
    public async Task Performance_GetByIdAsync_LargeDataset()
    {
        const int targetId = 50_000;

        var ef     = await MeasureEf        (r => r.GetByIdAsync(targetId)!, WarmUp, TimedSingleRow);
        var dapper = await MeasureDapper    (r => r.GetByIdAsync(targetId)!, WarmUp, TimedSingleRow);
        var raw    = await MeasureRawDapper (r => r.GetByIdAsync(targetId)!, WarmUp, TimedSingleRow);

        Report("GetByIdAsync (PK lookup, row 50 000)", TimedSingleRow, ef, dapper, raw);

        Assert.True(ef     / TimedSingleRow <= CeilSingleRow, $"EF Core GetByIdAsync too slow: {ef/TimedSingleRow}");
        Assert.True(dapper / TimedSingleRow <= CeilSingleRow, $"Dapper  GetByIdAsync too slow: {dapper/TimedSingleRow}");
        Assert.True(raw    / TimedSingleRow <= CeilSingleRow, $"Raw Dapper GetByIdAsync too slow: {raw/TimedSingleRow}");
    }

    [Fact]
    public async Task Performance_GetByCredentialAsync_LargeDataset()
    {
        const string email    = "user50000@example.com";
        const string password = "pass50000";

        var ef     = await MeasureEf        (r => r.GetByCredentialAsync(email, password)!, WarmUp, TimedSingleRow);
        var dapper = await MeasureDapper    (r => r.GetByCredentialAsync(email, password)!, WarmUp, TimedSingleRow);
        var raw    = await MeasureRawDapper (r => r.GetByCredentialAsync(email, password)!, WarmUp, TimedSingleRow);

        Report("GetByCredentialAsync (table scan — no index on Email)", TimedSingleRow, ef, dapper, raw);

        Assert.True(ef     / TimedSingleRow <= CeilSingleRow, $"EF Core GetByCredentialAsync too slow: {ef/TimedSingleRow}");
        Assert.True(dapper / TimedSingleRow <= CeilSingleRow, $"Dapper  GetByCredentialAsync too slow: {dapper/TimedSingleRow}");
        Assert.True(raw    / TimedSingleRow <= CeilSingleRow, $"Raw Dapper GetByCredentialAsync too slow: {raw/TimedSingleRow}");
    }

    [Fact]
    public async Task Performance_GetUserWithTenants_LargeDataset()
    {
        const int targetId = 50_000;

        var ef     = await MeasureEf        (r => r.GetUserWithTenants(targetId)!, WarmUp, TimedSingleRow);
        var dapper = await MeasureDapper    (r => r.GetUserWithTenants(targetId)!, WarmUp, TimedSingleRow);
        var raw    = await MeasureRawDapper (r => r.GetUserWithTenants(targetId)!, WarmUp, TimedSingleRow);

        Report("GetUserWithTenants (3-table join, row 50 000)", TimedSingleRow, ef, dapper, raw);

        Assert.True(ef     / TimedSingleRow <= CeilSingleRow, $"EF Core GetUserWithTenants too slow: {ef/TimedSingleRow}");
        Assert.True(dapper / TimedSingleRow <= CeilSingleRow, $"Dapper  GetUserWithTenants too slow: {dapper/TimedSingleRow}");
        Assert.True(raw    / TimedSingleRow <= CeilSingleRow, $"Raw Dapper GetUserWithTenants too slow: {raw/TimedSingleRow}");
    }
}

file static class LargeEnumerableExtensions
{
    public static Task AsTask<T>(this Task<IEnumerable<T>> task) => task;
}
