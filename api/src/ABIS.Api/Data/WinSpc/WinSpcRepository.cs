using System.Data.Common;
using Dapper;

namespace Abis.Api.Data.WinSpc;

/// <summary>Read-only access to the WinSPC quality database. All lookups resolve an ABIS key
/// (job or coil number) through WinSPC's free-text "tags" to the underlying dimensional
/// measurements + spec limits.</summary>
public interface IWinSpcRepository
{
    bool Enabled { get; }
    /// <summary>Opens a connection and runs a trivial probe. Returns null on success, else the
    /// error message (so the health endpoint can report why WinSPC is unreachable).</summary>
    Task<string?> CheckAsync(CancellationToken ct);
    /// <summary>WinSPC QC results for an ABIS job number; null when the connector is disabled.</summary>
    Task<WinSpcQc?> GetJobQcAsync(string jobNumber, CancellationToken ct);
    /// <summary>WinSPC QC results for an ABIS coil number; null when the connector is disabled.</summary>
    Task<WinSpcQc?> GetCoilQcAsync(string coilNumber, CancellationToken ct);
}

public sealed class WinSpcRepository : IWinSpcRepository
{
    private readonly IWinSpcConnectionFactory _factory;
    private readonly WinSpcOptions _options;

    // A single job/coil resolves to at most a few hundred readings in practice; cap defensively
    // so a mis-keyed lookup (e.g. a tag value shared by many subgroups) can't stream unbounded.
    private const int MaxReadings = 5000;

    // Portable across SQL Server (production) and SQLite (the test mimic): @-prefixed params,
    // UPPER/LIKE/LTRIM/RTRIM only. The tag match is an EXISTS over WinSPC's tag chain
    // (VTAGVAL → TAGVALUE → OPTTAG); the tag NAME is matched by pattern (operator-authored
    // variants all contain JOB/COIL) and the tag VALUE equals the trimmed ABIS key.
    private const string ReadingsSql = """
        SELECT s.DATETIME_ AS ReadingAt, p.PARTNAME AS PartName, v.VARIABLENAME AS Characteristic,
               s.VALUE_ AS Reading, v.LSLVALUE AS Lsl, v.TARGETVALUE AS Target, v.USLVALUE AS Usl, v.UNITS AS Units
        FROM VSAMPLE s
        JOIN VARBLE v ON v.VARIABLEID = s.VARIABLEID
        JOIN PART   p ON p.PARTID = v.PARTID
        WHERE EXISTS (
            SELECT 1 FROM VTAGVAL vt
            JOIN TAGVALUE tv ON tv.TAGVALUEID = vt.TAGVALUEID
            JOIN OPTTAG   o  ON o.TAGID = tv.TAGID
            WHERE vt.VARIABLEID = s.VARIABLEID AND vt.SUBGROUPNUMBER = s.SUBGROUPNUMBER
              AND UPPER(o.TAGNAME) LIKE @tagPattern
              AND LTRIM(RTRIM(tv.TAGVALUE)) = @key
        )
        ORDER BY s.DATETIME_ DESC
        """;

    public WinSpcRepository(IWinSpcConnectionFactory factory, WinSpcOptions options)
    {
        _factory = factory;
        _options = options;
    }

    public bool Enabled => _factory.Enabled;

    public async Task<string?> CheckAsync(CancellationToken ct)
    {
        if (!Enabled) return "WinSPC connector is disabled (WinSpc:Enabled=false or no connection string).";
        try
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);
            await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM PART", commandTimeout: _options.CommandTimeoutSeconds, cancellationToken: ct));
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public Task<WinSpcQc?> GetJobQcAsync(string jobNumber, CancellationToken ct) =>
        QueryAsync("job", jobNumber, _options.JobTagPattern, ct);

    public Task<WinSpcQc?> GetCoilQcAsync(string coilNumber, CancellationToken ct) =>
        QueryAsync("coil", coilNumber, _options.CoilTagPattern, ct);

    private async Task<WinSpcQc?> QueryAsync(string kind, string key, string tagPattern, CancellationToken ct)
    {
        if (!Enabled) return null;
        key = (key ?? "").Trim();

        await using DbConnection conn = _factory.Create();
        await conn.OpenAsync(ct);
        var rows = (await conn.QueryAsync<WinSpcReading>(new CommandDefinition(
            ReadingsSql, new { tagPattern, key }, commandTimeout: _options.CommandTimeoutSeconds, cancellationToken: ct))).AsList();

        if (rows.Count > MaxReadings) rows = rows.GetRange(0, MaxReadings);

        int inSpec = 0, outOfSpec = 0;
        foreach (var r in rows)
        {
            r.Dimension = WinSpcCharacteristicMap.ToDimension(r.Characteristic);
            r.InSpec = ComputeInSpec(r.Reading, r.Lsl, r.Usl);
            if (r.InSpec == true) inSpec++;
            else if (r.InSpec == false) outOfSpec++;
        }

        return new WinSpcQc
        {
            Key = key,
            KeyKind = kind,
            TotalReadings = rows.Count,
            InSpecReadings = inSpec,
            OutOfSpecReadings = outOfSpec,
            Readings = rows,
        };
    }

    /// <summary>Pass/fail = reading within the spec window. A missing bound is treated as open on
    /// that side (WinSPC leaves an unused limit null); null only when both bounds are absent.</summary>
    internal static bool? ComputeInSpec(double? reading, double? lsl, double? usl)
    {
        if (reading is not { } r) return null;
        if (lsl is null && usl is null) return null;
        if (lsl is { } lo && r < lo) return false;
        if (usl is { } hi && r > hi) return false;
        return true;
    }
}
