using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Oracle.ManagedDataAccess.Client;

namespace Abis.Api.Data;

/// <summary>Which database engine the API talks to.</summary>
public enum SqlDialect
{
    Sqlite,
    Oracle
}

/// <summary>Bound from the "Database" configuration section.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>"Sqlite" (dev/CI fixture) or "Oracle" (production).</summary>
    public string Provider { get; set; } = "Sqlite";

    public string ConnectionString { get; set; } = "Data Source=abis_dev.db";

    /// <summary>When true (dev only), create and seed the SQLite fixture at startup.</summary>
    public bool Seed { get; set; }

    /// <summary>Oracle only: format used to derive a sequence name from the table's
    /// <em>id column</em> when no explicit mapping is given. Default <c>{0}_seq</c>
    /// (e.g. id column <c>coil_abc_num</c> → <c>coil_abc_num_seq</c>, which matches
    /// the live ABIS convention <c>COIL_ABC_NUM_SEQ</c> — Oracle identifiers are
    /// case-insensitive). Confirmed against the DBO schema (see DATA_MODEL.md).</summary>
    public string SequenceNameFormat { get; set; } = "{0}_seq";

    /// <summary>Oracle only: explicit <c>table → sequence name</c> overrides for
    /// names that don't follow <see cref="SequenceNameFormat"/> (set via the
    /// <c>Database:Sequences</c> config section).</summary>
    public Dictionary<string, string> Sequences { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tables that have <em>no</em> Oracle sequence for their id column, so
    /// the id is assigned by <c>MAX(id)+1</c> on Oracle too (the legacy behaviour for
    /// such tables, e.g. <c>maint_log</c>). Low-volume tables only — MAX+1 is not
    /// concurrency-safe. Set via the <c>Database:MaxIdTables</c> config section.</summary>
    public HashSet<string> MaxIdTables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Oracle only: on startup, advance any id sequence that has drifted at/below its table's
    /// MAX(id) to MAX+1, so id-minting writes can't collide (ORA-00001) after a Data Pump refresh
    /// leaves the sequences behind. Idempotent; default on. Set <c>Database:ResyncSequencesOnStartup</c>
    /// false only if the app's DB user lacks ALTER SEQUENCE (then run tools/resync_sequences.sql as DBO).</summary>
    public bool ResyncSequencesOnStartup { get; set; } = true;

    public SqlDialect Dialect => Provider.Trim().ToLowerInvariant() switch
    {
        "oracle" => SqlDialect.Oracle,
        "sqlite" => SqlDialect.Sqlite,
        _ => throw new InvalidOperationException(
            $"Unsupported Database:Provider '{Provider}'. Use 'Sqlite' or 'Oracle'.")
    };
}

/// <summary>Creates ready-to-open <see cref="DbConnection"/>s for the configured engine.</summary>
public interface IDbConnectionFactory
{
    SqlDialect Dialect { get; }
    DbConnection Create();

    /// <summary>Wraps an ordered query with the engine-specific pagination.
    /// SQLite expects <c>:limit</c>/<c>:offset</c> parameters; Oracle (an 11g-compatible
    /// ROWNUM form) expects <c>:maxRow</c> (offset + pageSize) then <c>:minRow</c> (offset).</summary>
    string Paginate(string orderedSql);

    /// <summary>Dialect-specific SQL that yields the next id for an insert:
    /// <c>MAX+1</c> on SQLite (fine for the single-writer dev fixture), a sequence
    /// <c>NEXTVAL</c> on Oracle (concurrency-safe for production). <paramref name="table"/>
    /// and <paramref name="idColumn"/> are internal constants, never user input.
    /// <paramref name="sequence"/> forces a specific Oracle sequence (used for a
    /// second generated column on one table, e.g. shipment's bill_of_lading), bypassing
    /// the table-keyed override; it is validated like any other sequence name. Tables in
    /// <see cref="DatabaseOptions.MaxIdTables"/> use MAX+1 on Oracle as well (no sequence).</summary>
    string NextIdQuery(string table, string idColumn, string? sequence = null);

    /// <summary>Dialect-specific trivial connectivity probe. Oracle requires a FROM
    /// clause (a bare <c>SELECT 1</c> raises ORA-00923), so it uses <c>FROM dual</c>.</summary>
    string PingQuery { get; }

    /// <summary>The Oracle sequence a table's id column is minted from, resolved exactly as
    /// <see cref="NextIdQuery"/> does — or <c>null</c> when the table uses <c>MAX+1</c> (it is in
    /// <see cref="DatabaseOptions.MaxIdTables"/>, has no sequence) or the dialect isn't Oracle. Lets
    /// the startup sequence-resync read the same mapping the inserts use.</summary>
    string? SequenceFor(string table, string idColumn);
}

public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly DatabaseOptions _options;

    /// <summary>
    /// Bind Oracle parameters by NAME rather than by position. <b>Defence in depth, not a bug fix.</b>
    /// </summary>
    /// <remarks>
    /// <para>ODP.NET defaults <c>BindByName</c> to false, so an <c>OracleCommand</c> matches parameters
    /// to placeholders by ORDER. That sounds alarming next to a parameter object whose member order
    /// differs from the SQL — but <b>Dapper reorders parameters to match the SQL text before the
    /// command is executed</b>, so the mismatch never reaches the driver. Verified against the live
    /// Oracle both ways: six scrambled members, and a <c>DynamicParameters</c> built in the wrong order
    /// (the exact shape used by <c>PartParams</c>), both bind correctly with <c>BindByName=false</c>.</para>
    /// <para>It is set anyway for two narrow reasons: it removes the codebase's reliance on that Dapper
    /// implementation detail, and it turns a genuine name mismatch into an immediate error rather than
    /// something that depends on ordering behaviour. It fixes no known defect.</para>
    /// <para>It does NOT help with reserved-word bind names — <c>:from</c>, <c>:by</c> and friends fail
    /// at parse time with ORA-01745 whatever the binding mode. <c>OracleBindNameTests</c> guards those,
    /// and that IS a real, live-confirmed defect class.</para>
    /// <para>Placement is load-bearing: ODP.NET throws ORA-50099 if the property is set after any
    /// connection has opened, and this class is the only place the app constructs one, so the type
    /// initialiser is guaranteed to run first.</para>
    /// </remarks>
    static DbConnectionFactory() => OracleConfiguration.BindByName = true;

    public DbConnectionFactory(DatabaseOptions options) => _options = options;

    public SqlDialect Dialect => _options.Dialect;

    public DbConnection Create() => Dialect switch
    {
        SqlDialect.Sqlite => new SqliteConnection(_options.ConnectionString),
        SqlDialect.Oracle => new OracleConnection(_options.ConnectionString),
        _ => throw new InvalidOperationException($"Unsupported dialect {Dialect}.")
    };

    public string Paginate(string orderedSql) => Dialect switch
    {
        SqlDialect.Sqlite => $"{orderedSql} LIMIT :limit OFFSET :offset",
        // Oracle 11g-compatible ROWNUM pagination. The 12c+ "OFFSET :offset ROWS
        // FETCH NEXT :limit ROWS ONLY" clause raises ORA-00933 on 11g, so wrap the
        // ordered query instead: the inner inline view preserves ORDER BY, ROWNUM is
        // assigned over the ordered rows and capped at :maxRow, then the outer query
        // discards the first :minRow rows. :maxRow is bound before :minRow to match
        // the order the repository appends them in (Oracle positional binding). Works
        // on 11g and 12c+ alike. The inline-view alias must start with a letter —
        // Oracle rejects a leading underscore with ORA-00911.
        SqlDialect.Oracle =>
            $"SELECT * FROM (SELECT pg.*, ROWNUM AS rnum FROM ({orderedSql}) pg WHERE ROWNUM <= :maxRow) WHERE rnum > :minRow",
        _ => throw new InvalidOperationException($"Unsupported dialect {Dialect}.")
    };

    public string PingQuery => Dialect switch
    {
        SqlDialect.Sqlite => "SELECT 1",
        SqlDialect.Oracle => "SELECT 1 FROM dual",
        _ => throw new InvalidOperationException($"Unsupported dialect {Dialect}.")
    };

    public string NextIdQuery(string table, string idColumn, string? sequence = null)
    {
        // Tables with no Oracle sequence (Database:MaxIdTables) use MAX+1 on both
        // engines — the legacy behaviour for such tables (e.g. maint_log).
        var maxId = $"SELECT COALESCE(MAX({idColumn}), 0) + 1 FROM {table}";
        if (Dialect == SqlDialect.Oracle && _options.MaxIdTables.Contains(table))
            return maxId;

        return Dialect switch
        {
            // Single-writer dev fixture: MAX+1 is adequate and keeps the seed ids tidy.
            SqlDialect.Sqlite => maxId,
            // Production: a real sequence avoids the MAX+1 race under concurrent writers.
            // An explicit sequence (validated) overrides the table-keyed resolution.
            SqlDialect.Oracle => $"SELECT {(sequence is null ? ResolveSequence(table, idColumn) : ValidateSequence(sequence, table))}.NEXTVAL FROM dual",
            _ => throw new InvalidOperationException($"Unsupported dialect {Dialect}.")
        };
    }

    /// <summary>Resolves the Oracle sequence for a table: an explicit per-table
    /// override (<c>Database:Sequences</c>) if present, else the
    /// <see cref="DatabaseOptions.SequenceNameFormat"/> convention applied to the
    /// table's <paramref name="idColumn"/> (the live ABIS convention is
    /// <c>{ID_COLUMN}_SEQ</c>). Validates it is a plain (optionally schema-qualified)
    /// identifier before interpolation.</summary>
    private string ResolveSequence(string table, string idColumn)
    {
        var name = _options.Sequences.TryGetValue(table, out var mapped) && !string.IsNullOrWhiteSpace(mapped)
            ? mapped
            : string.Format(_options.SequenceNameFormat, idColumn);
        return ValidateSequence(name, table);
    }

    public string? SequenceFor(string table, string idColumn) =>
        Dialect != SqlDialect.Oracle || _options.MaxIdTables.Contains(table)
            ? null
            : ResolveSequence(table, idColumn);

    /// <summary>Ensures a sequence name is a plain (optionally schema-qualified)
    /// identifier before it is interpolated into SQL.</summary>
    private static string ValidateSequence(string name, string table)
    {
        if (!Regex.IsMatch(name, @"^[A-Za-z][A-Za-z0-9_$]*(\.[A-Za-z][A-Za-z0-9_$]*)?$"))
            throw new InvalidOperationException(
                $"Invalid Oracle sequence name '{name}' for table '{table}'. " +
                "Use a plain or schema-qualified identifier (Database:Sequences or Database:SequenceNameFormat).");
        return name;
    }
}
