using System.Text.RegularExpressions;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// Guards a defect class SQLite CI structurally cannot see: an <c>INSERT</c> that omits a column which is
/// <b>NOT NULL on Oracle</b>. Oracle rejects it with <c>ORA-01400</c> and the write never lands; SQLite
/// accepts it, because the fixture's DDL is far laxer than the real schema — 190 columns are NOT NULL on
/// live Oracle while nullable in <c>SqliteFixture</c>. No test could fail.
/// <para>This has already cost the project twice, each time found only by running against a real Oracle:
/// <c>ERROR_EVT.ERROR_USER</c>/<c>ERROR_TYPE_ID</c> (the DAS reverse path) and <c>sheet_tare_wt</c> on
/// skid creation. Both were single columns discovered one at a time.</para>
/// <para>The repository is currently <b>clean</b> — every app INSERT covers its table's NOT NULL columns —
/// so this guard starts with no exemption list and is here to keep it that way.</para>
/// </summary>
public sealed class OracleNotNullInsertTests
{
    /// <summary>NOT NULL columns of the live Oracle schema, keyed by table. Read from the committed
    /// snapshot rather than a live connection so CI needs no database.</summary>
    private static Dictionary<string, HashSet<string>> OracleNotNull()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(Path.Combine(RepoRoot(), "api", "tests", "ABIS.Api.Tests", "oracle-not-null.tsv")))
        {
            if (line.StartsWith('#') || line.Length == 0) continue;
            var parts = line.Split('\t');
            if (parts.Length != 2) continue;
            if (!map.TryGetValue(parts[0], out var cols))
                map[parts[0]] = cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            cols.Add(parts[1]);
        }
        Assert.NotEmpty(map);
        return map;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "api", "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string RepositorySource() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "api", "src", "ABIS.Api", "Data", "AbisRepository.cs"));

    /// <summary>Resolve the <c>{XxxInsertCols}</c> placeholders used to share long column lists between
    /// the create and copy paths. Each is a const built from one or more concatenated string literals.</summary>
    private static string ExpandTemplates(string columnList, string source)
    {
        for (var pass = 0; pass < 4 && columnList.Contains('{'); pass++)
            foreach (Match t in Regex.Matches(columnList, @"\{(\w+)\}"))
            {
                var decl = Regex.Match(source,
                    @"(?:const|static\s+readonly)\s+string\s+" + Regex.Escape(t.Groups[1].Value) + @"\s*=\s*(.*?);",
                    RegexOptions.Singleline);
                if (!decl.Success) continue;
                var literal = string.Join(" ", Regex.Matches(decl.Groups[1].Value, "\"([^\"]*)\"")
                                                    .Select(m => m.Groups[1].Value));
                columnList = columnList.Replace(t.Value, literal);
            }
        return columnList;
    }

    [Fact]
    public void No_insert_omits_a_column_that_is_NOT_NULL_on_Oracle()
    {
        var oracle = OracleNotNull();
        var source = RepositorySource();
        var offenders = new List<string>();

        foreach (Match m in Regex.Matches(source, @"INSERT\s+INTO\s+(\w+)\s*\(([^)]*)\)",
                                          RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var table = m.Groups[1].Value;
            if (!oracle.TryGetValue(table, out var required)) continue;   // not an Oracle table

            var list = ExpandTemplates(m.Groups[2].Value, source);

            // A column list still holding a placeholder is built at RUNTIME from the table's own schema
            // (the coil and part clone paths read every column and copy it verbatim), so it cannot be
            // checked statically — and cannot omit a column either, by construction.
            if (list.Contains('{')) continue;

            var present = list.Split(',').Select(c => c.Trim().Trim('"')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = required.Where(c => !present.Contains(c)).OrderBy(c => c).ToList();
            if (missing.Count > 0)
                offenders.Add($"{table} (line {source[..m.Index].Count(ch => ch == '\n') + 1}) omits: {string.Join(", ", missing)}");
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} INSERT statement(s) omit a column that is NOT NULL on Oracle. Each raises " +
            "ORA-01400 on the plant floor while passing on SQLite, because the CI fixture's DDL is laxer " +
            "than the real schema. Add the column to the INSERT (and give it a value that cannot be " +
            "null).\n\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_snapshot_covers_the_tables_this_project_has_actually_been_bitten_by()
    {
        // Regression on the guard itself. If the snapshot were ever regenerated against the wrong schema
        // (an empty user, a different service) it would silently shrink to nothing and guard nothing —
        // these are columns whose NOT NULL-ness cost real production failures.
        var oracle = OracleNotNull();
        foreach (var (table, column) in new[]
                 {
                     ("error_evt", "error_user"), ("error_evt", "error_type_id"),
                     ("sheet_skid", "sheet_tare_wt"), ("coil", "lot_num"), ("coil", "net_wt"),
                 })
            Assert.True(oracle.TryGetValue(table, out var cols) && cols.Contains(column),
                $"{table}.{column} is NOT NULL on the live database but missing from the snapshot.");
    }

    [Fact]
    public void The_template_expander_actually_resolves_the_shared_column_lists()
    {
        // ExpandTemplates silently returning the placeholder unchanged would make the main test skip the
        // three biggest INSERTs (order, order item, customer) and pass vacuously.
        var source = RepositorySource();
        foreach (var name in new[] { "OrderInsertCols", "OrderItemInsertCols", "CustomerWriteCols" })
        {
            var expanded = ExpandTemplates("{" + name + "}", source);
            Assert.DoesNotContain('{', expanded);
            Assert.Contains(',', expanded);
        }
    }
}
