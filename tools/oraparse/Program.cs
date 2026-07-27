using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;

// Parse every literal repository statement against the REAL Oracle schema without executing it.
// DBMS_SQL.PARSE compiles the statement — resolving tables, columns and syntax — and stops there;
// nothing runs until DBMS_SQL.EXECUTE, which is never called. Safe against the prod-derived .230.
var cs = Environment.GetEnvironmentVariable("ORA_CS");
if (string.IsNullOrWhiteSpace(cs)) { Console.WriteLine("ORA_CS not set"); return; }

var json = File.ReadAllText(args.Length > 0 ? args[0] : "sql_statements.json");
var stmts = JsonSerializer.Deserialize<List<Stmt>>(json);

await using var conn = new OracleConnection(cs);
await conn.OpenAsync();

int ok = 0; var failures = new List<(int Line, string Err, string Sql)>();
foreach (var s in stmts)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        DECLARE c INTEGER;
        BEGIN
          c := DBMS_SQL.OPEN_CURSOR;
          BEGIN
            DBMS_SQL.PARSE(c, :sql, DBMS_SQL.NATIVE);
            DBMS_SQL.CLOSE_CURSOR(c);
          EXCEPTION WHEN OTHERS THEN
            DBMS_SQL.CLOSE_CURSOR(c);
            RAISE;
          END;
        END;";
    cmd.Parameters.Add(new OracleParameter("sql", OracleDbType.Clob) { Value = s.sql });
    try { await cmd.ExecuteNonQueryAsync(); ok++; }
    catch (OracleException e)
    {
        var m = e.Message.Split('\n')[0].Trim();
        failures.Add((s.line, m, s.sql));
    }
}

Console.WriteLine($"parsed OK : {ok}/{stmts.Count}");
Console.WriteLine($"FAILED    : {failures.Count}");
foreach (var f in failures)
{
    Console.WriteLine($"\n--- AbisRepository.cs:{f.Line}");
    Console.WriteLine($"    {f.Err}");
    var head = f.Sql.Length > 160 ? f.Sql.Substring(0, 160).Replace("\n", " ") + " ..." : f.Sql.Replace("\n", " ");
    Console.WriteLine($"    {head}");
}

record Stmt(int line, string sql);
