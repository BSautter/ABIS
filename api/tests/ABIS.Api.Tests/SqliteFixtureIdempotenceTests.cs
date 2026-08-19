using System.Text.RegularExpressions;
using Abis.Api.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The SQLite fixture has to be safe to run <b>twice</b>.
///
/// <para>Every local <c>dotnet run</c> with <c>Database:Seed=true</c> re-seeds an existing
/// <c>abis_dev.db</c>. Nineteen tables were created but never dropped, so the second run died on the
/// first of them — <c>"table order_coil already exists"</c> — and the app would not start until
/// someone deleted the file. It cost two debugging sessions before it was recognised as a pattern
/// rather than a one-off.</para>
///
/// <para><b>CI could never have caught it.</b> Every test gets a fresh temp database, so the fixture
/// is only ever run once per file and the second-run path is exercised by nobody except a developer
/// on their own machine.</para>
/// </summary>
public sealed class SqliteFixtureIdempotenceTests
{
    [Fact]
    public void Seeding_the_same_database_twice_succeeds()
    {
        // The actual regression, end to end. Everything below only explains it.
        var path = Path.Combine(Path.GetTempPath(), $"abis_idem_{Guid.NewGuid():N}.db");
        var cs = $"Data Source={path}";
        try
        {
            SqliteFixture.EnsureCreatedAndSeeded(cs);
            SqliteFixture.EnsureCreatedAndSeeded(cs);   // this is the one that used to throw

            using var conn = new SqliteConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'";
            Assert.True(Convert.ToInt32(cmd.ExecuteScalar()) > 100, "the second seed should leave a full schema");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Every_table_the_fixture_CREATES_it_also_DROPS()
    {
        // The guard. Adding a CREATE without its DROP is a one-line omission that breaks nothing until
        // someone runs the app twice, and the failure names only the first offending table — which is
        // why nineteen of them accumulated behind `order_coil`.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "api", "src", "ABIS.Api", "Data", "SqliteFixture.cs"));

        var created = Regex.Matches(source, @"CREATE TABLE (?:IF NOT EXISTS )?([a-z_0-9]+)\s*\(")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dropped = Regex.Matches(source, @"DROP TABLE IF EXISTS ([a-z_0-9]+);")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(created.Count > 100, $"only {created.Count} CREATEs parsed — the scan is broken, not the fixture");

        var missing = created.Except(dropped).OrderBy(t => t).ToList();
        Assert.True(missing.Count == 0,
            $"{missing.Count} table(s) are CREATEd but never DROPped, so re-seeding an existing database " +
            "throws \"table X already exists\" and the app will not start:\n" + string.Join("\n", missing));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "api", "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
