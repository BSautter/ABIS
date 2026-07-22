using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace Abis.Api.Data.WinSpc;

/// <summary>
/// Configuration for the secondary, <b>read-only</b> WinSPC (DataNet SPC) quality database —
/// the plant's statistical-process-control system on RSEDAM-PC (SQL Server instance
/// <c>localhost\SQLEXPRESS</c>, database <c>WinSPC</c>). ABIS never writes here; it only reads
/// dimensional measurements + spec limits + pass/fail to surface QC against a job/coil.
/// Bound from the "WinSpc" configuration section. Disabled by default so CI and any deployment
/// that hasn't wired up a read-only SQL login are unaffected.
/// </summary>
public sealed class WinSpcOptions
{
    public const string SectionName = "WinSpc";

    /// <summary>Master switch. When false (default) the connector is inert and the endpoints
    /// report "not configured" rather than attempting a connection.</summary>
    public bool Enabled { get; set; }

    /// <summary>"SqlServer" (production, the WinSPC box) or "Sqlite" (the test mimic).</summary>
    public string Provider { get; set; } = "SqlServer";

    /// <summary>The full ADO.NET connection string for the read-only login (e.g.
    /// <c>Server=rsedam-pc\SQLEXPRESS;Database=WinSPC;User Id=abis_ro;Password=...;TrustServerCertificate=True</c>).
    /// Held in configuration/secrets on the ABIS side; ABIS never sees the operator's Windows login.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>SQL <c>LIKE</c> pattern (matched against <c>UPPER(OPTTAG.TAGNAME)</c>) that
    /// identifies the tag carrying the ABIS job number. WinSPC tag names are free-text with many
    /// operator-authored variants ("Job #", "Job No.", "AB Job No.", "ABJobNo." …) — every one
    /// contains "JOB", so the default catches them all without enumerating each.</summary>
    public string JobTagPattern { get; set; } = "%JOB%";

    /// <summary>As <see cref="JobTagPattern"/>, for the coil-number tag ("Coil #", "Coil No." …).</summary>
    public string CoilTagPattern { get; set; } = "%COIL%";

    public WinSpcDialect Dialect => Provider.Trim().ToLowerInvariant() switch
    {
        "sqlserver" => WinSpcDialect.SqlServer,
        "sqlite" => WinSpcDialect.Sqlite,
        _ => throw new InvalidOperationException(
            $"Unsupported WinSpc:Provider '{Provider}'. Use 'SqlServer' or 'Sqlite'.")
    };
}

public enum WinSpcDialect { SqlServer, Sqlite }

/// <summary>Creates ready-to-open read-only connections to the WinSPC database.</summary>
public interface IWinSpcConnectionFactory
{
    /// <summary>True when WinSPC is configured (<c>WinSpc:Enabled</c> + a connection string).</summary>
    bool Enabled { get; }
    DbConnection Create();
}

public sealed class WinSpcConnectionFactory : IWinSpcConnectionFactory
{
    private readonly WinSpcOptions _options;

    public WinSpcConnectionFactory(WinSpcOptions options) => _options = options;

    public bool Enabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.ConnectionString);

    public DbConnection Create() => _options.Dialect switch
    {
        WinSpcDialect.SqlServer => new SqlConnection(_options.ConnectionString),
        WinSpcDialect.Sqlite => new SqliteConnection(_options.ConnectionString),
        _ => throw new InvalidOperationException($"Unsupported WinSpc dialect {_options.Dialect}.")
    };
}
