using Abis.Api.Data;
using Abis.Api.Models;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Integration tests for the 997 functional-acknowledgment monitor (the modern P_CHECK_997 + Templar
/// reconcile), against a freshly seeded SQLite fixture.</summary>
public sealed class Edi997MonitorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AbisRepository _repo;

    public Edi997MonitorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"abis_997_{Guid.NewGuid():N}.db");
        var options = new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}", Seed = true };
        SqliteFixture.EnsureCreatedAndSeeded(options.ConnectionString);
        _repo = new AbisRepository(new DbConnectionFactory(options));
    }

    // A standards-correct fixed-width 106-byte ISA followed by one FA group acking control number `gcn`.
    private static string Ack997(long gcn, string setId, string code)
    {
        const char s = '*', t = '~';
        var isa = $"ISA{s}00{s}          {s}00{s}          {s}ZZ{s}{"PARTNER".PadRight(15)}{s}ZZ{s}" +
                  $"{"039630926".PadRight(15)}{s}260719{s}1200{s}U{s}00401{s}{"1".PadLeft(9, '0')}{s}0{s}P{s}>{t}";
        var body = $"GS{s}FA{s}PARTNER{s}039630926{s}20260719{s}1200{s}{gcn}{s}X{s}004010{t}" +
                   $"ST{s}997{s}0001{t}AK1{s}SH{s}{gcn}{t}AK2{s}{setId}{s}{gcn}{t}AK5{s}{code}{t}" +
                   $"AK9{s}{code}{s}1{s}1{s}{(code == "R" ? 0 : 1)}{t}SE{s}6{s}0001{t}GE{s}1{s}{gcn}{t}";
        return isa + body + $"IEA{s}1{s}000000001{t}";
    }

    [Fact]
    public async Task Waiting_lists_unacked_and_buckets_by_age()
    {
        // Seed row 9002 is un-acked (fa_received_time NULL); read its real transaction_time and bucket relative to it.
        var tx = await _repo.GetEdiTransactionAsync(9002, CancellationToken.None);
        var t = tx!.TransactionTime!.Value;

        // < 2h: the ack window is still open → "fresh".
        var fresh = await _repo.GetEdi997WaitingAsync(1, 200, null, t.AddMinutes(30), CancellationToken.None);
        Assert.Equal("fresh", fresh.Items.Single(i => i.EdiFileId == 9002).Bucket);

        // 2–24h: what legacy chased → "waiting". Population counts (not just the page) put it in WaitingCount.
        var waiting = await _repo.GetEdi997WaitingAsync(1, 200, null, t.AddHours(5), CancellationToken.None);
        var w = waiting.Items.Single(i => i.EdiFileId == 9002);
        Assert.Equal("waiting", w.Bucket);
        Assert.Equal("870", w.TransactionTypeId);
        Assert.Equal(5.0, w.AgeHours, 2);
        Assert.Equal(1, waiting.WaitingCount);
        Assert.Equal(0, waiting.OverdueCount);

        // > 24h: past the window → "overdue" (and it falls out of the fresh/waiting scan).
        var overdue = await _repo.GetEdi997WaitingAsync(1, 200, null, t.AddHours(30), CancellationToken.None);
        Assert.Equal("overdue", overdue.Items.Single(i => i.EdiFileId == 9002).Bucket);
        Assert.Equal(0, overdue.WaitingCount);
        Assert.Equal(1, overdue.OverdueCount);

        // The seeded already-acked transaction (9001) is never listed as waiting.
        Assert.DoesNotContain(fresh.Items, i => i.EdiFileId == 9001);
    }

    [Fact]
    public async Task Ingest_matches_by_control_number_stamps_ack_and_drops_from_waiting()
    {
        // Seed row 9002 is un-acked with group_control_number 2002 (edi_file_id 9002). Ack it as accepted.
        var now = new DateTime(2026, 3, 2, 10, 0, 0);
        var result = await _repo.IngestEdi997Async(Ack997(2002, "870", "A"), "997_in_test.x12", now, CancellationToken.None);

        Assert.Equal(1, result.AcksParsed);
        Assert.Equal(1, result.Matched);
        Assert.Equal(0, result.Unmatched);
        Assert.Equal(1, result.Accepted);
        var d = Assert.Single(result.Details);
        Assert.True(d.Matched);
        Assert.Equal(9002, d.EdiFileId);
        Assert.Equal("870", d.TransactionTypeId);
        Assert.Equal("Accepted", d.AckLabel);
        Assert.False(d.WasAlreadyAcked);

        // The ledger row is stamped acknowledged.
        var tx = await _repo.GetEdiTransactionAsync(9002, CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(tx!.FaReceivedTime));
        Assert.Equal(1, tx.FaReceiveStatus);
        Assert.Equal("997_in_test.x12", tx.FaReceivedFileName);

        // And it no longer shows up in the waiting monitor.
        var waiting = await _repo.GetEdi997WaitingAsync(1, 200, null, now.AddHours(50), CancellationToken.None);
        Assert.DoesNotContain(waiting.Items, i => i.EdiFileId == 9002);
    }

    [Fact]
    public async Task Ingest_handles_rejected_matches_and_unmatched_acks_together()
    {
        var now = new DateTime(2026, 3, 3, 9, 0, 0);
        // A rejected ack for a known control number (2002 → 9002) plus an accepted ack for an unknown one; the
        // parser scans every AK1/AK9 group across both concatenated envelopes.
        var raw = Ack997(2002, "870", "R") + Ack997(999999, "861", "A");

        var result = await _repo.IngestEdi997Async(raw, "mixed.x12", now, CancellationToken.None);

        Assert.Equal(2, result.AcksParsed);
        Assert.Equal(1, result.Matched);      // only 2002 exists
        Assert.Equal(1, result.Unmatched);    // 999999 does not
        Assert.Equal(1, result.Rejected);
        Assert.Equal(0, result.Accepted);

        var tx = await _repo.GetEdiTransactionAsync(9002, CancellationToken.None);
        Assert.Equal(2, tx!.FaReceiveStatus); // rejected
        Assert.False(string.IsNullOrEmpty(tx.FaReceivedTime));
    }

    [Fact]
    public async Task Reingesting_an_ack_reports_it_was_already_acked()
    {
        var now = new DateTime(2026, 3, 4, 11, 0, 0);
        await _repo.IngestEdi997Async(Ack997(2002, "870", "A"), "first.x12", now, CancellationToken.None);
        var second = await _repo.IngestEdi997Async(Ack997(2002, "870", "A"), "second.x12", now.AddHours(1), CancellationToken.None);

        Assert.Equal(1, second.Matched);
        Assert.Equal(1, second.AlreadyAcked);
        Assert.True(Assert.Single(second.Details).WasAlreadyAcked);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
