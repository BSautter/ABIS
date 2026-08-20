using System.Reflection;
using System.Text.RegularExpressions;
using Abis.Api.Data;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// Enforces that every id the app mints from an Oracle sequence is covered by the startup self-heal.
/// <para>A Data Pump refresh of the non-prod database imports rows but leaves sequences behind their
/// new table max, so <c>seq.NEXTVAL</c> returns an id that already exists and every insert fails with
/// ORA-00001. <c>ResyncSequencesAsync</c> exists to repair that on boot — but it works from a
/// hand-maintained list whose only protection was a "KEEP IN STEP" comment.</para>
/// <para>The comment lost. Three sequences were missing from it, and on 2026-07-25 <b>all three were
/// sitting behind their table max on the live database</b> — including the one every finished-sheet
/// write mints from, and one that was 827,368 behind. The self-heal would have reported success while
/// skipping them.</para>
/// </summary>
public sealed class SequenceResyncCoverageTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "api", "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>The (table, idColumn) pairs the resync covers, read from the real private field so the
    /// test can never drift from the list it is guarding.</summary>
    private static HashSet<(string Table, string Column)> Covered()
    {
        var field = typeof(AbisSchema).GetField("SequenceBackedTables", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var rows = (System.Collections.IEnumerable)field!.GetValue(null)!;
        var set = new HashSet<(string, string)>();
        foreach (var row in rows)
        {
            var t = row.GetType();
            set.Add(((string)t.GetField("Item1")!.GetValue(row)!, (string)t.GetField("Item2")!.GetValue(row)!));
        }
        return set;
    }

    [Fact]
    public void Every_NextIdAsync_call_site_is_either_MAX_plus_1_or_covered_by_the_resync()
    {
        var repo = File.ReadAllText(Path.Combine(RepoRoot(), "api", "src", "ABIS.Api", "Data", "AbisRepository.cs"));
        var sites = Regex.Matches(repo, @"NextIdAsync\(conn, tx, ""([a-z_]+)"", ""([a-z_]+)""")
            .Select(m => (Table: m.Groups[1].Value, Column: m.Groups[2].Value))
            .Distinct().ToList();
        Assert.NotEmpty(sites);

        // Tables that mint MAX+1 have no sequence and cannot drift.
        var maxId = new HashSet<string>(new DatabaseOptions().MaxIdTables, StringComparer.OrdinalIgnoreCase);
        var cfg = Path.Combine(RepoRoot(), "api", "src", "ABIS.Api", "appsettings.json");
        foreach (Match m in Regex.Matches(File.ReadAllText(cfg), @"""MaxIdTables""\s*:\s*\[(.*?)\]", RegexOptions.Singleline))
            foreach (Match t in Regex.Matches(m.Groups[1].Value, @"""([a-z_]+)"""))
                maxId.Add(t.Groups[1].Value);

        var covered = Covered();
        var missing = sites
            .Where(s => !maxId.Contains(s.Table) && !covered.Contains(s))
            .Select(s => $"{s.Table}.{s.Column}")
            .OrderBy(x => x)
            .ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} id(s) are minted from an Oracle sequence that the startup self-heal never " +
            "resyncs. After a Data Pump refresh those sequences sit behind their table max and EVERY " +
            "insert on them fails with ORA-00001, while the self-heal reports success. Add them to " +
            "AbisSchema.SequenceBackedTables (with an explicit sequence name if the table-keyed " +
            "resolution would pick the wrong one), or to Database:MaxIdTables if they genuinely mint " +
            "MAX+1.\n\n" + string.Join("\n", missing));
    }

    [Fact]
    public void The_shared_packaging_ticket_sequence_is_covered_for_both_of_its_tables()
    {
        // It is drawn via PackingItemCfg rather than NextIdAsync, so the scan above cannot see it —
        // and it was the worst offender found live, 827,368 behind its max.
        var covered = Covered();
        Assert.Contains(("sheet_packing_item", "sheet_packaging_ticket"), covered);
        Assert.Contains(("reject_coil_packing_item", "rej_coil_packaging_ticket"), covered);
    }

    [Fact]
    public void Bill_of_lading_is_resynced_against_its_OWN_sequence()
    {
        // `shipment` maps to PACKING_LIST_NUM_SEQ via Database:Sequences, so a table-keyed entry would
        // silently resync the wrong sequence and leave BILL_OF_LADING_SEQ behind.
        var field = typeof(AbisSchema).GetField("SequenceBackedTables", BindingFlags.NonPublic | BindingFlags.Static);
        var rows = (System.Collections.IEnumerable)field!.GetValue(null)!;
        string? explicitSeq = null;
        foreach (var row in rows)
        {
            var t = row.GetType();
            if ((string)t.GetField("Item1")!.GetValue(row)! == "shipment"
             && (string)t.GetField("Item2")!.GetValue(row)! == "bill_of_lading")
                explicitSeq = (string?)t.GetField("Item3")!.GetValue(row);
        }
        Assert.Equal("bill_of_lading_seq", explicitSeq);
    }

    /// <summary>
    /// The script's three arrays are POSITIONAL and must be the same length.
    ///
    /// <para>They were not. <c>seqs</c> carried 24 entries while <c>tbls</c> and <c>cols</c> carried
    /// 21, so the loop — which runs to <c>seqs.COUNT</c> and reads <c>tbls(i)</c> — raised ORA-06533
    /// on i=22..24. The loop's own <c>WHEN OTHERS</c> swallowed it and printed a bland "SKIPPED" line,
    /// indistinguishable from a sequence that genuinely does not exist.</para>
    ///
    /// <para><b>The three it skipped were the three added because they were badly behind</b> —
    /// PROD_ITEM_NUM 1,403, BILL_OF_LADING 167, SHEET_PACKAGING_TICKET 827,368. The script has
    /// therefore never once fixed the drift it was extended to fix, and it reported success while not
    /// doing so. The existing test above compares only the sequence NAMES, so it passed throughout.</para>
    /// </summary>
    [Fact]
    public void The_resync_scripts_three_arrays_are_the_same_length()
    {
        var sql = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "resync_sequences.sql"));

        int Count(string name)
        {
            var m = Regex.Match(sql, name + @"\s+t_map := t_map\((.*?)\);", RegexOptions.Singleline);
            Assert.True(m.Success, $"could not find the {name} array — the scan is broken, not the script");
            var body = Regex.Replace(m.Groups[1].Value, "--.*$", "", RegexOptions.Multiline);   // strip comments
            return body.Split(',').Count(x => x.Trim().Length > 0);
        }

        var seqs = Count("seqs");
        var tbls = Count("tbls");
        var cols = Count("cols");
        Assert.True(seqs > 20, $"only {seqs} sequences parsed — the scan is broken");
        Assert.True(seqs == tbls && seqs == cols,
            $"resync_sequences.sql's arrays are positional and must match: seqs={seqs}, tbls={tbls}, " +
            $"cols={cols}. A mismatch surfaces only as an ORA-06533 the loop swallows as SKIPPED, " +
            "so the affected sequences are silently never re-synced.");
    }

    /// <summary>The standalone <c>tools/resync_sequences.sql</c> lists the same sequences the app does.
    /// <para>The comment on <c>SequenceBackedTables</c> says to keep the two in step and calls that
    /// ENFORCED — it was not. The tests here compared the C# list against the <c>NextIdAsync</c> call
    /// sites and never opened the SQL file, so the operator's fallback script could fall behind the
    /// app silently. It happens to agree today; nothing was making it.</para>
    /// <para>The script matters precisely when the app cannot self-heal: it is what a DBA runs when the
    /// application's user lacks ALTER SEQUENCE. A sequence missing from it is one that stays behind its
    /// table max, and every insert minting from it fails with ORA-00001.</para></summary>
    [Fact]
    public void The_standalone_resync_script_lists_the_same_sequences_as_the_app()
    {
        var sql = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "resync_sequences.sql"));
        var listed = Regex.Matches(sql, @"'([A-Z_]+_SEQ)'")
            .Select(m => m.Groups[1].Value.ToUpperInvariant()).ToHashSet();
        Assert.NotEmpty(listed);

        // Resolve each covered table the way the app does — through the factory, so the
        // Database:Sequences overrides (error_evt -> ERROR_EVT_SEQ, dt_instance -> DT_INSTANCE_SEQ,
        // shipment -> PACKING_LIST_NUM_SEQ, ...) are applied rather than guessed.
        var options = new DatabaseOptions { Provider = "Oracle" };
        var cfgText = File.ReadAllText(Path.Combine(RepoRoot(), "api", "src", "ABIS.Api", "appsettings.json"));
        using (var doc = System.Text.Json.JsonDocument.Parse(cfgText))
        {
            var db = doc.RootElement.GetProperty("Database");
            if (db.TryGetProperty("Sequences", out var seqs))
                foreach (var pr in seqs.EnumerateObject())
                    options.Sequences[pr.Name] = pr.Value.GetString()!;
            if (db.TryGetProperty("MaxIdTables", out var mit))
                options.MaxIdTables = mit.EnumerateArray().Select(e => e.GetString()!).ToHashSet();
        }
        var factory = new DbConnectionFactory(options);

        var resolved = new HashSet<string>();
        foreach (var (table, column, explicitSeq) in CoveredWithSequence())
            resolved.Add((explicitSeq ?? factory.SequenceFor(table, column)!).ToUpperInvariant());

        var missing = resolved.Except(listed).OrderBy(x => x).ToList();
        var stale = listed.Except(resolved).OrderBy(x => x).ToList();

        Assert.True(missing.Count == 0,
            "tools/resync_sequences.sql is missing sequences the app mints from. That script is what a " +
            "DBA runs when the application's user lacks ALTER SEQUENCE, so anything absent stays behind " +
            "its table max and every insert on it fails with ORA-00001: " + string.Join(", ", missing));
        Assert.True(stale.Count == 0,
            "tools/resync_sequences.sql lists sequences the app no longer mints from: " + string.Join(", ", stale));
    }

    /// <summary>Every covered row as (table, column, explicit sequence or null).</summary>
    private static List<(string Table, string Column, string? Sequence)> CoveredWithSequence()
    {
        var field = typeof(AbisSchema).GetField("SequenceBackedTables", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var rows = (System.Collections.IEnumerable)field!.GetValue(null)!;
        var list = new List<(string, string, string?)>();
        foreach (var row in rows)
        {
            var t = row.GetType();
            list.Add(((string)t.GetField("Item1")!.GetValue(row)!,
                      (string)t.GetField("Item2")!.GetValue(row)!,
                      (string?)t.GetField("Item3")!.GetValue(row)));
        }
        return list;
    }
}
