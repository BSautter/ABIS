using System.Collections.Concurrent;

namespace Abis.Api.Middleware;

/// <summary>
/// Serialises work on one resource so a check and the write it guards cannot be interleaved.
///
/// <para><b>What it is for.</b> <c>WithIfMatch</c> reads the entity, compares the caller's validator,
/// then updates — three steps that were not atomic. Two callers holding the same current validator
/// could both pass the comparison, and the second would overwrite the first: the exact loss the
/// precondition exists to prevent, in the microseconds between the check and the write.</para>
///
/// <para><b>Two limits, stated plainly.</b></para>
/// <para>1. <b>It is per process.</b> ABIS runs as a single systemd service, so this covers the whole
/// deployment today — but it stops working the moment a second instance is put behind a load balancer,
/// silently and with no failure to notice. Anything scaling ABIS out must replace this with a
/// database-level compare-and-swap first.</para>
/// <para>2. <b>It does not protect against the legacy application</b>, which writes the same tables
/// (12 write sites against <c>coil</c>, 9 against <c>shipment</c>). Nothing does: legacy sends no
/// preconditions and overwrites unconditionally, so even a row lock would only order the writes, not
/// stop the loss. <c>If-Match</c> is a contract between ABIS clients, and that is the race this
/// closes.</para>
/// </summary>
public sealed class ResourceLock
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int Waiters;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Take the lock for <paramref name="key"/>; dispose the result to release it.
    /// <para>Entries are reference-counted and removed when the last holder leaves, so a long-running
    /// service does not accumulate one semaphore per record it has ever touched.</para></summary>
    public async Task<IAsyncDisposable> AcquireAsync(string key, CancellationToken ct)
    {
        var entry = _entries.AddOrUpdate(key,
            _ => { var e = new Entry(); e.Waiters = 1; return e; },
            (_, e) => { Interlocked.Increment(ref e.Waiters); return e; });

        try
        {
            await entry.Gate.WaitAsync(ct);
        }
        catch
        {
            Release(key, entry);   // cancelled while queued — do not leak the reference
            throw;
        }
        return new Handle(this, key, entry);
    }

    private void Release(string key, Entry entry)
    {
        if (Interlocked.Decrement(ref entry.Waiters) == 0)
            // Racing an incoming AddOrUpdate is harmless: the loser simply creates a fresh entry, and
            // correctness only requires that everyone holding the SAME entry is mutually excluded.
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
    }

    private sealed class Handle(ResourceLock owner, string key, Entry entry) : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                entry.Gate.Release();
                owner.Release(key, entry);
            }
            return ValueTask.CompletedTask;
        }
    }
}
