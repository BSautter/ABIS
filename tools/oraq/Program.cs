// oraq — a tiny READ-ONLY Oracle query CLI.
//
// Purpose: run ad-hoc SELECTs against the ABIS Oracle databases (data-dictionary
// discovery, schema checks) without a full IDE. It is the safe workhorse for the
// read-only cron/job discovery across the prod (192.168.1.9) and dev (192.168.1.11)
// boxes — see docs/ADMIN_SUBSYSTEM_PLAN.md. It HARD-REFUSES anything that is not a
// single SELECT/WITH so it can be pointed at live production without risk.
//
// Usage:
//   dotnet run --project tools/oraq -- --cs "Data Source=host:1521/svc;User Id=u;Password=p;" --sql "SELECT ..."
//   echo "SELECT ..." | dotnet run --project tools/oraq -- --cs "..."           # SQL from stdin
//   ORA_CS="..." dotnet run --project tools/oraq -- --sql "SELECT ..."          # conn string from env
// Options: --json (JSON rows), --timeout <sec> (default 60), --max <rows> (default 500).

using System.Text;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;

static string? Opt(string[] a, string name)
{
    var i = Array.IndexOf(a, name);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
}

var cs = Opt(args, "--cs") ?? Environment.GetEnvironmentVariable("ORA_CS");
var sql = Opt(args, "--sql");
var asJson = args.Contains("--json");
var timeout = int.TryParse(Opt(args, "--timeout"), out var t) ? t : 60;
var max = int.TryParse(Opt(args, "--max"), out var m) ? m : 500;

if (string.IsNullOrWhiteSpace(sql) && Console.IsInputRedirected)
    sql = Console.In.ReadToEnd();

if (string.IsNullOrWhiteSpace(cs)) { Console.Error.WriteLine("error: no connection string (--cs or ORA_CS)."); return 2; }
if (string.IsNullOrWhiteSpace(sql)) { Console.Error.WriteLine("error: no SQL (--sql or stdin)."); return 2; }

// --- READ-ONLY guard -------------------------------------------------------------
// Strip a single trailing semicolon + trailing whitespace, then require the statement
// to begin with SELECT or WITH and contain no statement-terminating ';' and no
// data-changing / PL-SQL keywords. Defense-in-depth against stacked statements.
var stmt = sql.Trim();
stmt = Regex.Replace(stmt, @";\s*$", "");
if (stmt.Contains(';'))
{
    Console.Error.WriteLine("refused: multiple statements (';') are not allowed — read-only single SELECT only.");
    return 3;
}
if (!Regex.IsMatch(stmt, @"^\s*(select|with)\b", RegexOptions.IgnoreCase))
{
    Console.Error.WriteLine("refused: only SELECT/WITH queries are allowed (read-only).");
    return 3;
}
var forbidden = new[] { "insert", "update", "delete", "merge", "drop", "create", "alter",
    "truncate", "grant", "revoke", "begin", "declare", "call", "execute", "commit", "rollback",
    "lock", "into" };
foreach (var kw in forbidden)
    if (Regex.IsMatch(stmt, $@"\b{kw}\b", RegexOptions.IgnoreCase))
    {
        Console.Error.WriteLine($"refused: keyword '{kw}' not allowed in a read-only query.");
        return 3;
    }

try
{
    await using var conn = new OracleConnection(cs);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = stmt;
    cmd.CommandTimeout = timeout;
    await using var r = await cmd.ExecuteReaderAsync();

    var cols = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray();
    var rows = new List<string[]>();
    while (await r.ReadAsync() && rows.Count < max)
    {
        var row = new string[cols.Length];
        for (var i = 0; i < cols.Length; i++)
        {
            var v = r.IsDBNull(i) ? null : r.GetValue(i);
            row[i] = v switch
            {
                null => "",
                DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss"),
                // Binary columns (LONG RAW / RAW / BLOB) render as length + leading bytes rather than
                // "System.Byte[]". The length is the point: ODP.NET truncates a LONG silently when
                // InitialLONGFetchSize is too small, and the only way to notice is to look at it.
                byte[] b => $"byte[{b.Length}] {BitConverter.ToString(b, 0, Math.Min(8, b.Length))}",
                _ => v.ToString() ?? ""
            };
        }
        rows.Add(row);
    }

    if (asJson)
    {
        var sb = new StringBuilder("[\n");
        for (var ri = 0; ri < rows.Count; ri++)
        {
            sb.Append("  {");
            for (var ci = 0; ci < cols.Length; ci++)
            {
                sb.Append('"').Append(cols[ci]).Append("\":");
                sb.Append(System.Text.Json.JsonSerializer.Serialize(rows[ri][ci]));
                if (ci < cols.Length - 1) sb.Append(", ");
            }
            sb.Append('}').Append(ri < rows.Count - 1 ? ",\n" : "\n");
        }
        sb.Append(']');
        Console.WriteLine(sb.ToString());
    }
    else
    {
        var w = cols.Select((c, i) => Math.Max(c.Length, rows.Count == 0 ? 0 : rows.Max(row => row[i].Length))).ToArray();
        Console.WriteLine(string.Join(" | ", cols.Select((c, i) => c.PadRight(w[i]))));
        Console.WriteLine(string.Join("-+-", w.Select(x => new string('-', x))));
        foreach (var row in rows)
            Console.WriteLine(string.Join(" | ", row.Select((c, i) => c.PadRight(w[i]))));
    }
    Console.Error.WriteLine($"({rows.Count} row(s){(rows.Count >= max ? $", capped at {max}" : "")})");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
