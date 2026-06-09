using System.Diagnostics;
using DapperUserRepository = DAL.Repositories.UserRepository;
using EfUserRepository = DAL.EF.UserRepository;

namespace Tests;

/// <summary>
/// Performance comparison on a 100 000-row dataset.
///
/// Iteration counts are tuned per-method:
///   - GetAllAsync       : 3 timed runs — loads the full table each time, very heavy
///   - Single-row ops    : 20 timed runs — fast I/O, enough for stable averages
///
/// Assertions only check that neither ORM exceeds a generous ceiling so tests
/// stay green on slow CI machines.  Read the printed report for the actual
/// EF-vs-Dapper comparison.
/// </summary>
public class PerformanceLargeDatasetTests(LargeDatasetFixture fixture) : IClassFixture<LargeDatasetFixture>
{
    private const int WarmUp          = 2;
    private const int TimedBulk       = 3;   // full-table scan — keep low
    private const int TimedSingleRow  = 20;

    private static readonly TimeSpan CeilBulk      = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CeilSingleRow = TimeSpan.FromMilliseconds(200);

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<TimeSpan> MeasureEf(Func<EfUserRepository, Task> action, int warmUp, int timed)
    {
        for (var i = 0; i < warmUp; i++)
        {
            await using var ctx = fixture.CreateContext();
            await action(new EfUserRepository(ctx));
        }
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < timed; i++)
        {
            await using var ctx = fixture.CreateContext();
            await action(new EfUserRepository(ctx));
        }
        sw.Stop();
        return sw.Elapsed;
    }

    private async Task<TimeSpan> MeasureDapper(Func<DapperUserRepository, Task> action, int warmUp, int timed)
    {
        for (var i = 0; i < warmUp; i++)
            await action(new DapperUserRepository(fixture.CreateConnectionFactory()));

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < timed; i++)
            await action(new DapperUserRepository(fixture.CreateConnectionFactory()));
        sw.Stop();
        return sw.Elapsed;
    }

    private static void Report(string scenario, int iterations, TimeSpan ef, TimeSpan dapper)
    {
        var efMs     = ef.TotalMilliseconds     / iterations;
        var dapperMs = dapper.TotalMilliseconds / iterations;
        var ratio    = efMs / dapperMs;

        Console.WriteLine($"""

            ┌─ [100k rows] {scenario}  ({iterations} iterations)
            │  EF Core  : {ef.TotalMilliseconds,9:F1} ms total  ({efMs,8:F2} ms/call)
            │  Dapper   : {dapper.TotalMilliseconds,9:F1} ms total  ({dapperMs,8:F2} ms/call)
            │  EF/Dapper: {ratio,6:F2}x  ({(ratio > 1 ? "EF slower" : "EF faster")})
            └─────────────────────────────────────────────────────────
            """);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Performance_GetAllAsync_LargeDataset()
    {
        var ef     = await MeasureEf    (r => r.GetAllAsync().AsTask(), WarmUp, TimedBulk);
        var dapper = await MeasureDapper(r => r.GetAllAsync().AsTask(), WarmUp, TimedBulk);

        Report($"GetAllAsync ({LargeDatasetFixture.UserCount:N0} users)", TimedBulk, ef, dapper);

        Assert.True(ef     / TimedBulk <= CeilBulk,      $"EF Core GetAllAsync too slow: {ef/TimedBulk}");
        Assert.True(dapper / TimedBulk <= CeilBulk,      $"Dapper  GetAllAsync too slow: {dapper/TimedBulk}");
    }

    [Fact]
    public async Task Performance_GetByIdAsync_LargeDataset()
    {
        const int targetId = 50_000;

        var ef     = await MeasureEf    (r => r.GetByIdAsync(targetId)!, WarmUp, TimedSingleRow);
        var dapper = await MeasureDapper(r => r.GetByIdAsync(targetId)!, WarmUp, TimedSingleRow);

        Report("GetByIdAsync (PK lookup, row 50 000)", TimedSingleRow, ef, dapper);

        Assert.True(ef     / TimedSingleRow <= CeilSingleRow, $"EF Core GetByIdAsync too slow: {ef/TimedSingleRow}");
        Assert.True(dapper / TimedSingleRow <= CeilSingleRow, $"Dapper  GetByIdAsync too slow: {dapper/TimedSingleRow}");
    }

    [Fact]
    public async Task Performance_GetByCredentialAsync_LargeDataset()
    {
        const string email    = "user50000@example.com";
        const string password = "pass50000";

        var ef     = await MeasureEf    (r => r.GetByCredentialAsync(email, password)!, WarmUp, TimedSingleRow);
        var dapper = await MeasureDapper(r => r.GetByCredentialAsync(email, password)!, WarmUp, TimedSingleRow);

        Report("GetByCredentialAsync (table scan — no index on Email)", TimedSingleRow, ef, dapper);

        Assert.True(ef     / TimedSingleRow <= CeilSingleRow, $"EF Core GetByCredentialAsync too slow: {ef/TimedSingleRow}");
        Assert.True(dapper / TimedSingleRow <= CeilSingleRow, $"Dapper  GetByCredentialAsync too slow: {dapper/TimedSingleRow}");
    }

    [Fact]
    public async Task Performance_GetUserWithTenants_LargeDataset()
    {
        const int targetId = 50_000;

        var ef     = await MeasureEf    (r => r.GetUserWithTenants(targetId)!, WarmUp, TimedSingleRow);
        var dapper = await MeasureDapper(r => r.GetUserWithTenants(targetId)!, WarmUp, TimedSingleRow);

        Report("GetUserWithTenants (3-table join, row 50 000)", TimedSingleRow, ef, dapper);

        Assert.True(ef     / TimedSingleRow <= CeilSingleRow, $"EF Core GetUserWithTenants too slow: {ef/TimedSingleRow}");
        Assert.True(dapper / TimedSingleRow <= CeilSingleRow, $"Dapper  GetUserWithTenants too slow: {dapper/TimedSingleRow}");
    }
}

file static class LargeEnumerableExtensions
{
    public static Task AsTask<T>(this Task<IEnumerable<T>> task) => task;
}
