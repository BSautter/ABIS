using Abis.Api.Data.WinSpc;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ABIS.Api.Tests;

/// <summary>Pure-function coverage for the WinSPC characteristic→dimension map and the
/// in-spec rule (no database needed).</summary>
public class WinSpcMappingTests
{
    [Theory]
    [InlineData("Gauge", "gauge")]
    [InlineData("Gauge (A)", "gauge")]
    [InlineData("Thickness", "gauge")]
    [InlineData("Width", "width")]
    [InlineData("Reference Width", "width")]
    [InlineData("Square", "square")]
    [InlineData("Flatness", "flatness")]
    [InlineData("Feed Length", "feedLength")]
    [InlineData("Part Length #1 Operator Side", "lengthOper")]
    [InlineData("Part Length #2 Drive Side", "lengthDrive")]
    [InlineData("Diameter", "diameter")]
    [InlineData("Overall Length", "length")]
    [InlineData("Lengh", "length")]              // a real misspelled WinSPC characteristic
    public void Maps_characteristic_to_dimension(string name, string expected)
        => Assert.Equal(expected, WinSpcCharacteristicMap.ToDimension(name));

    [Theory]
    [InlineData("Oil")]
    [InlineData("Skid Count")]
    [InlineData("Weight")]
    [InlineData("")]
    [InlineData(null)]
    public void Unmapped_characteristics_return_null(string? name)
        => Assert.Null(WinSpcCharacteristicMap.ToDimension(name));

    [Theory]
    [InlineData(0.043, 0.042, 0.044, true)]   // within
    [InlineData(99.0, 63.9, 64.2, false)]     // over USL
    [InlineData(0.03, 0.042, 0.044, false)]   // under LSL
    [InlineData(5.0, null, 10.0, true)]       // only USL, within
    [InlineData(50.0, null, 10.0, false)]     // only USL, over
    [InlineData(5.0, null, null, null)]       // no spec → unknown
    public void Computes_in_spec(double reading, double? lsl, double? usl, bool? expected)
        => Assert.Equal(expected, WinSpcRepository.ComputeInSpec(reading, lsl, usl));

    [Fact]
    public void No_reading_is_unknown() => Assert.Null(WinSpcRepository.ComputeInSpec(null, 1, 2));
}

/// <summary>Exercises the WinSPC read query against a SQLite mimic of the real WinSPC tables
/// (PART/VARBLE/VSAMPLE + the OPTTAG→TAGVALUE→VTAGVAL tag chain), so the join, the free-text
/// tag matching, trimming, and the in-spec rollup are all covered without a live SQL Server.</summary>
public class WinSpcRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"winspc_{Guid.NewGuid():N}.db");
    private readonly WinSpcRepository _repo;

    public WinSpcRepositoryTests()
    {
        var cs = $"Data Source={_dbPath}";
        using (var conn = new SqliteConnection(cs))
        {
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE PART (PARTID INTEGER, PARTNAME TEXT);
                CREATE TABLE VARBLE (VARIABLEID INTEGER, VARIABLENAME TEXT, PARTID INTEGER, LSLVALUE REAL, TARGETVALUE REAL, USLVALUE REAL, UNITS TEXT);
                CREATE TABLE VSAMPLE (VARIABLEID INTEGER, SUBGROUPNUMBER INTEGER, SAMPLENUMBER INTEGER, USERID INTEGER, DATETIME_ TEXT, VALUE_ REAL);
                CREATE TABLE OPTTAG (TAGID INTEGER, TAGNAME TEXT, PARTID INTEGER, ISJOBTAG TEXT);
                CREATE TABLE TAGVALUE (TAGVALUEID INTEGER, TAGVALUE TEXT, TAGID INTEGER);
                CREATE TABLE VTAGVAL (VARIABLEID INTEGER, SUBGROUPNUMBER INTEGER, TAGVALUEID INTEGER);

                INSERT INTO PART VALUES (1, 'C6088 RH DOOR OTR');
                INSERT INTO VARBLE VALUES (10, 'Gauge', 1, 0.042, 0.043, 0.044, '');
                INSERT INTO VARBLE VALUES (11, 'Width', 1, 63.938, 64.095, 64.252, '');
                INSERT INTO VARBLE VALUES (12, 'Part Length #1 Operator Side', 1, 29.963, 30.061, 30.159, '');

                INSERT INTO VSAMPLE VALUES (10, 100, 1, 1, '2026-05-14 15:41:54', 0.043);   -- gauge, in spec
                INSERT INTO VSAMPLE VALUES (11, 101, 1, 1, '2026-05-14 15:42:03', 99.0);     -- width, OVER usl
                INSERT INTO VSAMPLE VALUES (12, 102, 1, 1, '2026-05-14 15:42:13', 30.0);     -- length, in spec

                -- 'Job #' tag = 124346 (with a leading space, to prove trimming) on all three subgroups;
                -- a 'Coil No.' tag only on subgroup 100.
                INSERT INTO OPTTAG VALUES (900, 'Job #', 1, 'F');
                INSERT INTO OPTTAG VALUES (901, 'Coil No.', 1, 'F');
                INSERT INTO TAGVALUE VALUES (5000, ' 124346', 900);
                INSERT INTO TAGVALUE VALUES (5001, 'F50043566', 901);
                INSERT INTO VTAGVAL VALUES (10, 100, 5000);
                INSERT INTO VTAGVAL VALUES (11, 101, 5000);
                INSERT INTO VTAGVAL VALUES (12, 102, 5000);
                INSERT INTO VTAGVAL VALUES (10, 100, 5001);
                """;
            cmd.ExecuteNonQuery();
        }
        var opts = new WinSpcOptions { Enabled = true, Provider = "Sqlite", ConnectionString = cs };
        _repo = new WinSpcRepository(new WinSpcConnectionFactory(opts), opts);
    }

    [Fact]
    public async Task GetJobQc_returns_readings_specs_dimensions_and_rollup()
    {
        var qc = await _repo.GetJobQcAsync("124346", CancellationToken.None);
        Assert.NotNull(qc);
        Assert.Equal("job", qc!.KeyKind);
        Assert.Equal(3, qc.TotalReadings);
        Assert.Equal(2, qc.InSpecReadings);
        Assert.Equal(1, qc.OutOfSpecReadings);

        var gauge = qc.Readings.Single(r => r.Characteristic == "Gauge");
        Assert.Equal("gauge", gauge.Dimension);
        Assert.Equal(0.044, gauge.Usl);
        Assert.True(gauge.InSpec);

        var width = qc.Readings.Single(r => r.Characteristic == "Width");
        Assert.False(width.InSpec);                       // 99 > USL 64.252
        Assert.Equal("C6088 RH DOOR OTR", width.PartName);

        Assert.Equal("lengthOper", qc.Readings.Single(r => r.Characteristic!.Contains("Operator")).Dimension);
    }

    [Fact]
    public async Task GetCoilQc_matches_only_the_coil_tag()
    {
        var qc = await _repo.GetCoilQcAsync("F50043566", CancellationToken.None);
        Assert.NotNull(qc);
        Assert.Equal("coil", qc!.KeyKind);
        Assert.Equal(1, qc.TotalReadings);                // only subgroup 100 carries the coil tag
    }

    [Fact]
    public async Task Unknown_job_yields_an_empty_result_not_null()
    {
        var qc = await _repo.GetJobQcAsync("999999", CancellationToken.None);
        Assert.NotNull(qc);
        Assert.Equal(0, qc!.TotalReadings);
    }

    [Fact]
    public async Task Disabled_connector_returns_null()
    {
        var opts = new WinSpcOptions { Enabled = false };
        var repo = new WinSpcRepository(new WinSpcConnectionFactory(opts), opts);
        Assert.False(repo.Enabled);
        Assert.Null(await repo.GetJobQcAsync("124346", CancellationToken.None));
        Assert.NotNull(await repo.CheckAsync(CancellationToken.None));   // returns the "disabled" message
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { /* temp file */ }
    }
}
