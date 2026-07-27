using System.Text.RegularExpressions;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// Guards the single most expensive defect class in this codebase: <b>SQL that passes CI and fails on
/// the plant floor.</b> Tests run against SQLite; production is Oracle 11g. A bind parameter named
/// after an Oracle reserved word is rejected at PARSE time with <c>ORA-01745</c> — the statement never
/// runs — but SQLite accepts it happily, so no test anywhere would catch it.
/// <para>This has bitten the project repeatedly (<c>:desc</c>, <c>:date</c>, <c>:by</c>, <c>:start</c>,
/// <c>:end</c>, <c>:set</c>, <c>:uid</c>), each time discovered only by running against a real Oracle.
/// Fixing the sites one at a time leaves the next one to be written next week, so the rule is enforced
/// here instead.</para>
/// </summary>
public sealed class OracleBindNameTests
{
    /// <summary>Oracle 11g reserved words (the ones that can plausibly appear as a bind name). A bind
    /// called any of these fails to parse.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACCESS", "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "AUDIT", "BETWEEN", "BY", "CHAR",
        "CHECK", "CLUSTER", "COLUMN", "COMMENT", "COMPRESS", "CONNECT", "CREATE", "CURRENT", "DATE",
        "DECIMAL", "DEFAULT", "DELETE", "DESC", "DISTINCT", "DROP", "ELSE", "EXCLUSIVE", "EXISTS",
        "FILE", "FLOAT", "FOR", "FROM", "GRANT", "GROUP", "HAVING", "IDENTIFIED", "IMMEDIATE", "IN",
        "INCREMENT", "INDEX", "INITIAL", "INSERT", "INTEGER", "INTERSECT", "INTO", "IS", "LEVEL",
        "LIKE", "LOCK", "LONG", "MAXEXTENTS", "MINUS", "MLSLABEL", "MODE", "MODIFY", "NOAUDIT",
        "NOCOMPRESS", "NOT", "NOWAIT", "NULL", "NUMBER", "OF", "OFFLINE", "ON", "ONLINE", "OPTION",
        "OR", "ORDER", "PCTFREE", "PRIOR", "PUBLIC", "RAW", "RENAME", "RESOURCE", "REVOKE", "ROW",
        "ROWID", "ROWNUM", "ROWS", "SELECT", "SESSION", "SET", "SHARE", "SIZE", "SMALLINT", "START",
        "SUCCESSFUL", "SYNONYM", "SYSDATE", "TABLE", "THEN", "TO", "TRIGGER", "UID", "UNION", "UNIQUE",
        "UPDATE", "USER", "VALIDATE", "VALUES", "VARCHAR", "VARCHAR2", "VIEW", "WHENEVER", "WHERE",
        "WITH",
        // Not on Oracle's SQL reserved-word list, but EMPIRICALLY rejected as a bind name on this
        // project's own live Oracle (docs/ORACLE_VALIDATION.md:121 — ":desc, :date, :by, :start, :end").
        // Observed behaviour beats the published list.
        "END",
    };

    /// <summary>A <c>:name</c> bind placeholder. Excludes <c>::</c> and anything preceded by a word
    /// character, so time literals ("10:30"), ratios and C# labels don't register.</summary>
    private static readonly Regex Bind = new(@"(?<![\w:]):([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "api", "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> SqlBearingFiles()
    {
        var root = RepoRoot();
        foreach (var dir in new[] { Path.Combine(root, "api", "src") })
            foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                yield return f;
    }

    /// <summary>Strip line comments so prose like "// bind :from here" doesn't register as SQL.</summary>
    private static string WithoutComments(string line)
    {
        var i = line.IndexOf("//", StringComparison.Ordinal);
        return i >= 0 ? line[..i] : line;
    }

    /// <summary>
    /// Sites that already carry a reserved-word bind, recorded when this guard was introduced. A
    /// RATCHET, not an exemption: the guard fails if anything NEW appears, and each entry is deleted as
    /// its site is fixed. The list may only ever shrink.
    /// <para>Fixing all of them in one commit would be a large, hard-to-review change across unrelated
    /// subsystems; the point of the guard is to stop the 24th being written while the existing 23 are
    /// burned down. <c>SqliteFixture</c> entries are the seed data, which never runs against Oracle —
    /// they are listed anyway so the rule stays absolute and needs no arguing about exceptions.</para>
    /// </summary>
    private static readonly HashSet<string> KnownOffenders = new(StringComparer.Ordinal)
    {
        // Only the SQLite seed data remains. It never runs against Oracle, but it is listed rather
        // than exempted so the rule stays absolute and nobody has to argue about which files count.
        "SqliteFixture.cs:982 :By",
        "SqliteFixture.cs:1576 :Start", "SqliteFixture.cs:1576 :End",
        "SqliteFixture.cs:1586 :Start", "SqliteFixture.cs:1586 :End",
        "SqliteFixture.cs:2071 :Start", "SqliteFixture.cs:2071 :End",
        "SqliteFixture.cs:2072 :By",
    };

    [Fact]
    public void No_bind_parameter_is_named_after_an_Oracle_reserved_word()
    {
        var offenders = new List<string>();
        foreach (var file in SqlBearingFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = WithoutComments(lines[i]);
                foreach (Match m in Bind.Matches(code))
                {
                    var name = m.Groups[1].Value;
                    if (!Reserved.Contains(name)) continue;
                    var site = $"{Path.GetFileName(file)}:{i + 1} :{name}";
                    if (!KnownOffenders.Contains(site)) offenders.Add(site);
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} NEW bind parameter(s) are named after Oracle reserved words. Each raises " +
            "ORA-01745 at parse time on Oracle 11g while passing silently on SQLite, so the statement " +
            "never runs in production. Rename the bind (e.g. :from -> :fromDt) AND the matching member " +
            "on the parameter object — and keep the member ORDER aligned with the placeholders, because " +
            "ODP.NET binds positionally.\n\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_known_offender_list_has_no_stale_entries()
    {
        // If a site is fixed but left listed, that line silently stops being guarded — and line numbers
        // shift as the file is edited, so a stale entry can end up shielding unrelated code.
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SqlBearingFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                foreach (Match m in Bind.Matches(WithoutComments(lines[i])))
                    if (Reserved.Contains(m.Groups[1].Value))
                        live.Add($"{Path.GetFileName(file)}:{i + 1} :{m.Groups[1].Value}");
        }

        var stale = KnownOffenders.Where(k => !live.Contains(k)).ToList();
        Assert.True(stale.Count == 0,
            "These entries are in KnownOffenders but no longer match a real bind — delete them, or the " +
            "line they name stops being guarded:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void The_reserved_word_list_actually_catches_the_traps_this_project_has_hit()
    {
        // Regression on the guard itself: every reserved word that has cost this project a live
        // Oracle failure must be in the list, or the guard silently stops guarding.
        foreach (var w in new[] { "desc", "date", "by", "start", "end", "set", "uid", "from", "to", "between", "like" })
            Assert.True(Reserved.Contains(w), $":{w} has caused a real ORA-01745 here but is not in the reserved list.");
    }

    [Fact]
    public void The_bind_pattern_does_not_fire_on_things_that_are_not_binds()
    {
        // Guard against the guard being noisy: a false positive here would train people to ignore it.
        foreach (var notABind in new[] { "10:30", "a::b", "http://x", "ratio 3:1", "x:=1" })
            Assert.DoesNotContain(Bind.Matches(notABind).Cast<Match>(),
                m => Reserved.Contains(m.Groups[1].Value));
    }
}
