using System.Net;
using System.Net.Http.Json;
using Abis.Api.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// <c>If-Match</c> under genuine concurrency.
/// <para>#342 made clients send the header, which closed the large window — a form held open for
/// minutes now fails its save correctly. That left a small one: <c>WithIfMatch</c> read the entity,
/// compared the validator and updated as three separate steps, so two callers holding the SAME current
/// validator could both pass the comparison and the second would overwrite the first. Microseconds
/// wide, but it is exactly the loss the precondition exists to prevent.</para>
/// <para>These tests fire the saves concurrently. Without the fix the outcome is a coin toss and the
/// suite would be flaky rather than red — which is why the lock primitive is also tested directly,
/// where mutual exclusion can be asserted deterministically.</para>
/// </summary>
public class IfMatchConcurrencyTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_ifmatch_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
            builder.UseSetting("Database:Seed", "true");
            builder.UseSetting("ApiKeys:Enabled", "true");
            builder.UseSetting("ApiKeys:Keys:0", "test-key");
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }

    private static HttpClient Client(Factory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return c;
    }

    private static HttpRequestMessage Patch(string url, string etag, object body)
    {
        var r = new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonContent.Create(body) };
        r.Headers.TryAddWithoutValidation("If-Match", etag);
        return r;
    }

    [Fact]
    public async Task Concurrent_saves_holding_the_same_validator_leave_exactly_one_winner()
    {
        using var f = new Factory();
        var c = Client(f);

        var etag = (await c.GetAsync("/api/jobs/1001")).Headers.ETag!.ToString();

        // Eight saves fired together, every one holding the validator that was current when they
        // started — the shape of two operators pressing Save at the same moment.
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            c.SendAsync(Patch("/api/jobs/1001", etag, new { jobNotes = $"writer {i}" }))));

        var ok = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var refused = results.Count(r => r.StatusCode == HttpStatusCode.PreconditionFailed);

        Assert.Equal(1, ok);        // the first to get the lock
        Assert.Equal(7, refused);   // the rest find the row moved on, and are told so
        Assert.Equal(8, ok + refused);
    }

    [Fact]
    public async Task A_second_save_succeeds_once_it_re_reads()
    {
        // The lock must not leave the resource stuck: after a refusal, reading again and retrying is
        // the documented recovery and it has to work.
        using var f = new Factory();
        var c = Client(f);

        var first = (await c.GetAsync("/api/jobs/1001")).Headers.ETag!.ToString();
        Assert.Equal(HttpStatusCode.OK,
            (await c.SendAsync(Patch("/api/jobs/1001", first, new { jobNotes = "A" }))).StatusCode);

        Assert.Equal(HttpStatusCode.PreconditionFailed,
            (await c.SendAsync(Patch("/api/jobs/1001", first, new { jobNotes = "B" }))).StatusCode);

        var fresh = (await c.GetAsync("/api/jobs/1001")).Headers.ETag!.ToString();
        Assert.Equal(HttpStatusCode.OK,
            (await c.SendAsync(Patch("/api/jobs/1001", fresh, new { jobNotes = "B" }))).StatusCode);
    }

    [Fact]
    public async Task Saves_to_different_resources_are_not_serialised_against_each_other()
    {
        // The lock is per resource. Locking globally would turn every concurrent edit in the plant into
        // a queue behind one another.
        using var f = new Factory();
        var c = Client(f);

        var job = (await c.GetAsync("/api/jobs/1001")).Headers.ETag!.ToString();
        var coil = (await c.GetAsync("/api/coils/5001")).Headers.ETag!.ToString();

        var results = await Task.WhenAll(
            c.SendAsync(Patch("/api/jobs/1001", job, new { jobNotes = "job" })),
            c.SendAsync(Patch("/api/coils/5001", coil, new { coilNotes = "coil" })));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task A_request_without_a_precondition_is_unaffected()
    {
        // No If-Match means last-one-wins by design; it must not start failing, and it does not take
        // the lock at all.
        using var f = new Factory();
        var c = Client(f);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            c.PatchAsync("/api/jobs/1001", JsonContent.Create(new { jobNotes = $"n{i}" }))));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}

/// <summary>The lock primitive itself, where mutual exclusion can be asserted without racing.</summary>
public class ResourceLockTests
{
    [Fact]
    public async Task Only_one_holder_at_a_time_for_the_same_key()
    {
        var locks = new ResourceLock();
        var inside = 0;
        var maxSeen = 0;

        await Task.WhenAll(Enumerable.Range(0, 24).Select(async _ =>
        {
            await using var _h = await locks.AcquireAsync("same", CancellationToken.None);
            var now = Interlocked.Increment(ref inside);
            maxSeen = Math.Max(maxSeen, now);
            await Task.Delay(2);
            Interlocked.Decrement(ref inside);
        }));

        Assert.Equal(1, maxSeen);
    }

    [Fact]
    public async Task Different_keys_do_not_block_each_other()
    {
        var locks = new ResourceLock();
        await using var held = await locks.AcquireAsync("a", CancellationToken.None);

        // Must not need the first to be released — a timeout here means the lock is global, not keyed.
        var other = locks.AcquireAsync("b", CancellationToken.None);
        var done = await Task.WhenAny(other, Task.Delay(2000));
        Assert.Same(other, done);
        await using var _ = await other;
    }

    [Fact]
    public async Task Entries_do_not_accumulate_once_released()
    {
        // A long-running service must not keep one semaphore per record it has ever touched.
        var locks = new ResourceLock();
        for (var i = 0; i < 500; i++)
            await (await locks.AcquireAsync($"key-{i}", CancellationToken.None)).DisposeAsync();

        var field = typeof(ResourceLock).GetField("_entries",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var map = (System.Collections.ICollection)field.GetValue(locks)!;
        Assert.Empty(map);
    }

    [Fact]
    public async Task Releasing_twice_is_harmless()
    {
        var locks = new ResourceLock();
        var h = await locks.AcquireAsync("k", CancellationToken.None);
        await h.DisposeAsync();
        await h.DisposeAsync();   // must not over-release and let two holders in

        var inside = 0;
        var maxSeen = 0;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var _h = await locks.AcquireAsync("k", CancellationToken.None);
            maxSeen = Math.Max(maxSeen, Interlocked.Increment(ref inside));
            await Task.Delay(1);
            Interlocked.Decrement(ref inside);
        }));
        Assert.Equal(1, maxSeen);
    }
}
