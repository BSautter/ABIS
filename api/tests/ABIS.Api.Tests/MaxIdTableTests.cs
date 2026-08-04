using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// Guards the one way the <c>MAX(id)+1</c> id-minting path can lose data.
/// <para>Most tables mint ids from an Oracle sequence, which is atomic. The tables listed in
/// <c>Database:MaxIdTables</c> instead use <c>SELECT COALESCE(MAX(id),0)+1</c> on Oracle as well as
/// SQLite. That is a genuine race — two transactions can both read the same MAX before either commits
/// — and it is <b>deliberate</b>, not an oversight: the legacy PowerBuilder application still writes
/// most of these tables and assigns ids the same way, so minting from a sequence in the modern stack
/// would hand out ids legacy is about to reuse.</para>
/// <para>What makes the race survivable is the primary key: the loser of a collision gets
/// <c>ORA-00001</c> — a failed request, visible and safe. Verified against live <c>.230</c>: all
/// fourteen listed tables have one.</para>
/// <para>Add a table to that list <b>without</b> a PK and the character of the bug changes completely.
/// Two concurrent creates would then both succeed with the same id, and every later read, update and
/// join on that id silently addresses the wrong row. This test exists so that cannot be done quietly.</para>
/// </summary>
public sealed class MaxIdTableTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "api", "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Tables with a primary key on the live database, from the committed snapshot (so CI
    /// needs no Oracle connection).</summary>
    private static HashSet<string> TablesWithPrimaryKey()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(
                     Path.Combine(RepoRoot(), "api", "tests", "ABIS.Api.Tests", "oracle-primary-keys.tsv")))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('\t');
            if (parts.Length == 3) set.Add(parts[0]);
        }
        Assert.NotEmpty(set);
        return set;
    }

    /// <summary>The configured MAX+1 tables, read from appsettings.json so the guard tracks the real
    /// configuration rather than a copy of it.</summary>
    private static string[] MaxIdTables()
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot(), "api", "src", "ABIS.Api", "appsettings.json")));
        var list = doc.RootElement.GetProperty("Database").GetProperty("MaxIdTables");
        var tables = list.EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.NotEmpty(tables);
        return tables;
    }

    [Fact]
    public void Every_table_that_mints_its_id_with_MAX_plus_1_has_a_primary_key()
    {
        var pk = TablesWithPrimaryKey();
        var missing = MaxIdTables().Where(t => !pk.Contains(t)).OrderBy(t => t).ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} table(s) in Database:MaxIdTables have NO primary key on the live schema. " +
            "MAX(id)+1 is racy by nature; the primary key is the only thing turning a concurrent " +
            "collision into a clean ORA-00001 instead of two rows sharing an id, which every later " +
            "read and join would then resolve wrongly. Add the key, or mint from a sequence " +
            "instead.\n\n" + string.Join("\n", missing));
    }

    [Fact]
    public void The_snapshot_still_covers_the_tables_this_guard_is_about()
    {
        // A snapshot regenerated against the wrong schema or an empty user would shrink to nothing and
        // this guard would pass vacuously while protecting no one.
        var pk = TablesWithPrimaryKey();
        Assert.True(pk.Count > 300, $"Only {pk.Count} tables have a PK in the snapshot — that is far too few.");
        foreach (var t in new[] { "maint_log", "security_user", "abis_truck_appointment", "sheet_skid", "coil" })
            Assert.Contains(t, pk);
    }

    [Fact]
    public void The_configured_list_is_read_from_appsettings_and_is_not_empty()
    {
        // If the property were renamed or moved, MaxIdTables() would throw rather than silently return
        // nothing — but an empty array would still pass the main test while guarding nothing.
        var tables = MaxIdTables();
        Assert.Contains("maint_log", tables);
        Assert.Contains("abis_truck_appointment", tables);

        // Every entry must be a plain identifier: the value is interpolated into SQL as a table name
        // (NextIdQuery builds "SELECT COALESCE(MAX(id),0)+1 FROM {table}"), so anything else would be
        // injectable through configuration.
        foreach (var t in tables)
            Assert.Matches(new Regex(@"^[A-Za-z][A-Za-z0-9_]*$"), t);
    }
}
