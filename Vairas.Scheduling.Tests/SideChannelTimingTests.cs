using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Tests;

/// <summary>
/// Local timing measurement for docs/side-channel-test-plan.md §7 on real PostgreSQL (Testcontainers). It compares the
/// foreign-school learner conflict with the equivalent own-school learner conflict using paired, randomly ordered samples
/// and a robust statistic. It is evidence for this machine and build only, never a production latency guarantee.
/// </summary>
[TestFixture, Category("Integration"), Category("SideChannel"), NonParallelizable]
public sealed class SideChannelTimingTests
{
    public const int Pairs = 200, Warmup = 20, Seed = 91;
    public const double RelativeTolerance = 0.25, AbsoluteToleranceMs = 1.0;
    private PostgresFixture pg = null!;
    [OneTimeSetUp] public async Task Start() { pg = new(); await pg.Start(); }
    [OneTimeTearDown] public async Task Stop() { if (pg is not null) await pg.DisposeAsync(); }

    private sealed record Summary(string Comparison, int Pairs, double MedianAMs, double MedianBMs, double P10AMs, double P10BMs, double P90AMs, double P90BMs,
        double MedianPairedDifferenceMs, double CiLowMs, double CiHighMs, double RelativeDifference, double ShareASlower, double SignTestZ, double ToleranceMs, bool Asserted);

    [Test]
    public async Task Foreign_and_own_learner_conflicts_have_equivalent_latency_on_PostgreSQL()
    {
        var w = new TestWorld(); var s = pg.Service(w); var other = new TestWorld();
        // Different ISO weeks, so each accounting window holds exactly one occupied session and identical rule work.
        var foreignSlot = DateTimeOffset.Parse("2030-02-05T08:00:00Z"); var ownSlot = DateTimeOffset.Parse("2030-02-19T08:00:00Z"); var freeSlot = DateTimeOffset.Parse("2030-03-05T08:00:00Z");
        await pg.Service(other).CreateAsync(other.Practical(foreignSlot) with { StudentId = w.Student }, LessonKind.Practical, Guid.NewGuid(), other.Actor, default);
        await s.CreateAsync(w.Practical(ownSlot) with { TeacherId = Guid.NewGuid(), VehicleId = Guid.NewGuid() }, LessonKind.Practical, Guid.NewGuid(), w.Actor, default);

        async Task Book(DateTimeOffset slot)
        {
            try { await s.CreateAsync(w.Practical(slot), LessonKind.Practical, Guid.NewGuid(), w.Actor, default); }
            catch (BusinessException e) when (e.Code == "RESOURCE_UNAVAILABLE") { return; }
            Assert.Fail("Expected generic unavailability.");
        }
        async Task Query(DateTimeOffset slot, int expected)
        {
            var result = await s.AvailabilityAsync(w.Practical(slot), slot, slot.AddMinutes(45), w.Actor, default);
            if (result.Slots.Count != expected) Assert.Fail("Unexpected availability result.");
        }
        var results = new[]
        {
            await Compare("booking rejection: foreign vs own learner conflict", () => Book(foreignSlot), () => Book(ownSlot), true),
            await Compare("availability: foreign vs own learner conflict", () => Query(foreignSlot, 0), () => Query(ownSlot, 0), true),
            // Informational: free vs busy differs by content anyway (the approved disclosure); recorded, not asserted.
            await Compare("availability: free vs foreign-busy (informational)", () => Query(freeSlot, 1), () => Query(foreignSlot, 0), false)
        };
        var json = JsonSerializer.Serialize(new { measuredAt = DateTimeOffset.UtcNow, environment = $"{Environment.OSVersion}; {Environment.ProcessorCount} logical CPUs; .NET {Environment.Version}; PostgreSQL Testcontainers", pairs = Pairs, warmup = Warmup, seed = Seed, results }, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.Combine(PostgresFixture.Root, "evidence", "side-channel"));
        await File.WriteAllTextAsync(Path.Combine(PostgresFixture.Root, "evidence", "side-channel", "timing.json"), json);
        TestContext.Out.WriteLine(json);
        foreach (var r in results.Where(x => x.Asserted))
            Assert.That(Math.Abs(r.MedianPairedDifferenceMs), Is.LessThanOrEqualTo(r.ToleranceMs), r.Comparison);
    }

    private static async Task<Summary> Compare(string name, Func<Task> a, Func<Task> b, bool asserted)
    {
        var random = new Random(Seed); var ta = new List<double>(); var tb = new List<double>();
        for (var i = 0; i < Warmup + Pairs; i++)
        {
            // Interleaved pairs with random order cancel drift (cache warm-up, table growth, background load).
            double x, y;
            if (random.Next(2) == 0) { x = await Time(a); y = await Time(b); } else { y = await Time(b); x = await Time(a); }
            if (i >= Warmup) { ta.Add(x); tb.Add(y); }
        }
        var d = ta.Zip(tb, (x, y) => x - y).ToArray();
        var bootstrap = new Random(Seed); var medians = new double[2000];
        for (var i = 0; i < medians.Length; i++) medians[i] = Median(Enumerable.Range(0, d.Length).Select(_ => d[bootstrap.Next(d.Length)]).ToArray());
        Array.Sort(medians);
        var slower = d.Count(x => x > 0); var n = d.Count(x => x != 0);
        var medianB = Median(tb.ToArray());
        return new(name, Pairs, Round(Median(ta.ToArray())), Round(medianB), Round(Quantile(ta, 0.1)), Round(Quantile(tb, 0.1)), Round(Quantile(ta, 0.9)), Round(Quantile(tb, 0.9)),
            Round(Median(d)), Round(medians[49]), Round(medians[1949]), Math.Round(Median(d) / medianB, 4), Math.Round((double)slower / d.Length, 4),
            Math.Round(n == 0 ? 0 : (slower - n / 2.0) / Math.Sqrt(n / 4.0), 3), Round(Math.Max(RelativeTolerance * medianB, AbsoluteToleranceMs)), asserted);
    }
    private static async Task<double> Time(Func<Task> act) { var start = Stopwatch.GetTimestamp(); await act(); return Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    private static double Median(double[] values) => Quantile(values, 0.5);
    private static double Quantile(IEnumerable<double> values, double q)
    {
        var sorted = values.Order().ToArray(); var position = (sorted.Length - 1) * q; var low = (int)Math.Floor(position); var high = (int)Math.Ceiling(position);
        return sorted[low] + (sorted[high] - sorted[low]) * (position - low);
    }
    private static double Round(double value) => Math.Round(value, 3);
}
