using System.Net.Http.Json;
using Abis.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The edge-trim rules an order line must satisfy — legacy <c>w_order_entry.srw:505-545</c>.
///
/// <para>Two kinds of failure that must not be conflated: <b>hard errors</b> (missing widths, or a
/// trimmed width WIDER than the coil) which legacy refuses outright, and <b>outside the trimmer's
/// tolerance band</b>, which it offers to override. Only the second is a judgement call.</para>
/// </summary>
public sealed class EdgeTrimRuleTests
{
    private static readonly EdgeTrim.Tolerance Live = new(0.75m, 12.00m);   // the plant's actual band

    // ---- Hard errors: no override offered ------------------------------------------------

    [Fact]
    public void A_trimmed_width_WIDER_than_the_coil_is_a_hard_error_not_a_tolerance_question()
    {
        // You cannot trim a 48" coil down to 60". This is not "outside tolerance", it is not a coil —
        // so it must never reach the overridable branch.
        var err = EdgeTrim.HardError(incomingWidth: 48m, trimmedWidth: 60m, trimTypeCode: 1);
        Assert.NotNull(err);
        Assert.Contains("greater than", err);
    }

    [Theory]
    [InlineData(null, 48.0, 1, "incoming coil width")]
    [InlineData(50.0, null, 1, "trimmed coil width")]
    [InlineData(50.0, 48.0, null, "trim type")]
    public void A_missing_field_is_named(double? incoming, double? trimmed, int? trimType, string expected)
    {
        var err = EdgeTrim.HardError((decimal?)incoming, (decimal?)trimmed, trimType);
        Assert.NotNull(err);
        Assert.Contains(expected, err);
    }

    [Fact]
    public void A_coherent_line_has_no_hard_error()
    {
        Assert.Null(EdgeTrim.HardError(50.0m, 48.25m, 1));
    }

    // ---- The band ---------------------------------------------------------------------------

    [Fact]
    public void Both_bounds_are_INCLUSIVE()
    {
        // Legacy tests `difference < lower OR difference > upper`, so a trim of exactly the limit is
        // acceptable. A band edge is a number someone chose; refusing the exact configured value
        // would reject the setting itself.
        Assert.False(EdgeTrim.IsOutsideTolerance(0.75m, Live));
        Assert.False(EdgeTrim.IsOutsideTolerance(12.00m, Live));
        Assert.True(EdgeTrim.IsOutsideTolerance(0.74m, Live));
        Assert.True(EdgeTrim.IsOutsideTolerance(12.01m, Live));
    }

    [Fact]
    public void The_difference_is_incoming_minus_trimmed()
    {
        Assert.Equal(1.75m, EdgeTrim.Difference(50.00m, 48.25m));
        Assert.Null(EdgeTrim.Difference(null, 48.25m));
        Assert.Null(EdgeTrim.Difference(50.00m, null));
    }

    [Fact]
    public void An_UNKNOWN_difference_is_not_treated_as_out_of_tolerance()
    {
        // Nothing to judge. The hard-error path owns the missing-width case; this must not also
        // refuse it, or the caller gets two different messages for one mistake.
        Assert.False(EdgeTrim.IsOutsideTolerance(null, Live));
    }

    [Fact]
    public void The_refusal_names_BOTH_limits_and_the_actual_trim()
    {
        // Legacy prints both, so the operator can tell which end they are on and by how much.
        var msg = EdgeTrim.OutsideToleranceMessage(0.50m, Live);
        Assert.Contains("0.50", msg);
        Assert.Contains("0.75", msg);
        Assert.Contains("12.00", msg);
    }

    // ---- The fallback band --------------------------------------------------------------------

    [Fact]
    public void The_hardcoded_fallback_is_NOT_the_plants_real_band()
    {
        // Pinned deliberately. The source falls back to 1.500/12.000, but the live table reads
        // 0.75/12.00 — the comment trail shows "< 1" -> "< 0.75" (2016-12) -> "1.50-12.00" (2017-06),
        // and the table was later set back. Treating the fallback as the real band would demand an
        // override on every trim between 0.75" and 1.5" the plant accepts today.
        Assert.Equal(1.500m, EdgeTrim.LegacyFallback.LowerInches);
        Assert.Equal(12.000m, EdgeTrim.LegacyFallback.UpperInches);
        Assert.NotEqual(EdgeTrim.LegacyFallback.LowerInches, Live.LowerInches);
    }
}

/// <summary>The gate through the pipeline: what an order line save actually does.</summary>
public sealed class EdgeTrimGateEndpointTests : IClassFixture<EdgeTrimGateEndpointTests.Factory>
{
    private readonly HttpClient _client;
    public EdgeTrimGateEndpointTests(Factory f) => _client = f.Client();

    /// <summary>A trimmed line on order 9001, item 7001. `diff` is how much comes off.</summary>
    private static object Line(decimal diff, string? overridden = null) => new
    {
        enduserPartNum = "PN-3003-A",   // required by OrderItemWrite validation, before the gate runs
        sector = 1,                     // likewise — sector is required on every order line
        sheetType = "RECTANGLE",
        trimmingRequired = "Y",
        incomingCoilWidth = 50.00m,
        trimmedCoilWidth = 50.00m - diff,
        trimTypeCode = 2,
        trimmedWidthOverridden = overridden,
    };

    private async Task<HttpResponseMessage> Save(object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/orders/9001/items/7001")
        { Content = System.Net.Http.Json.JsonContent.Create(body) };
        req.Headers.Add("X-User-Login", "jsmith");
        return await _client.SendAsync(req);
    }

    [Fact]
    public async Task A_trim_INSIDE_the_band_saves()
    {
        Assert.Equal(System.Net.HttpStatusCode.OK, (await Save(Line(1.75m))).StatusCode);
    }

    [Fact]
    public async Task A_trim_OUTSIDE_the_band_is_refused_with_the_numbers()
    {
        // 0.50" off, against the seeded live band of 0.75-12.00.
        var res = await Save(Line(0.50m));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("0.75", body);
        Assert.Contains("12.00", body);
    }

    [Fact]
    public async Task The_same_trim_SAVES_when_explicitly_overridden()
    {
        Assert.Equal(System.Net.HttpStatusCode.OK, (await Save(Line(0.50m, overridden: "Y"))).StatusCode);
    }

    [Fact]
    public async Task Coming_back_INTO_tolerance_CLEARS_a_previous_override()
    {
        // The one that is easy to miss. Without it a line overridden once keeps the flag forever and
        // the job sheet goes on printing "CONTACT FOREMAN BEFORE RUNNING" in red on an item somebody
        // already corrected — and a warning that outlives its fault is one the floor learns to ignore.
        Assert.Equal(System.Net.HttpStatusCode.OK, (await Save(Line(0.50m, overridden: "Y"))).StatusCode);

        var back = await Save(Line(2.00m, overridden: "Y"));   // in tolerance, flag still sent
        Assert.Equal(System.Net.HttpStatusCode.OK, back.StatusCode);

        var item = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/orders/9001/items/7001");
        var flag = item.GetProperty("trimmedWidthOverridden");
        Assert.True(flag.ValueKind == System.Text.Json.JsonValueKind.Null
                    || string.IsNullOrWhiteSpace(flag.GetString()),
            "an item back inside tolerance must not still be flagged as overridden");
    }

    [Fact]
    public async Task A_trimmed_width_wider_than_the_coil_is_refused_even_WITH_an_override()
    {
        // The hard errors are checked first and carry no override — you cannot authorise a coil that
        // does not exist.
        var res = await Save(new
        {
            enduserPartNum = "PN-3003-A", sector = 1,
            sheetType = "RECTANGLE", trimmingRequired = "Y",
            incomingCoilWidth = 48.00m, trimmedCoilWidth = 60.00m, trimTypeCode = 2,
            trimmedWidthOverridden = "Y",
        });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("greater than", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_line_that_is_NOT_trimmed_is_not_gated_at_all()
    {
        var res = await Save(new { enduserPartNum = "PN-3003-A", sector = 1, sheetType = "RECTANGLE", trimmingRequired = "N" });
        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
    }

    public sealed class Factory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_trim_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
            builder.UseSetting("Database:Seed", "true");
            builder.UseSetting("ApiKeys:Enabled", "true");
            builder.UseSetting("ApiKeys:Keys:0", "test-key");
        }
        internal HttpClient Client()
        {
            var c = CreateClient();
            c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
            return c;
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }
}

/// <summary>Reading the band out of the database — the half that silently fell back once already.</summary>
public sealed class EdgeTrimToleranceReadTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AbisRepository _repo;
    private readonly string _cs;

    public EdgeTrimToleranceReadTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"abis_tol_{Guid.NewGuid():N}.db");
        _cs = $"Data Source={_dbPath}";
        SqliteFixture.EnsureCreatedAndSeeded(_cs);
        _repo = new AbisRepository(new DbConnectionFactory(new DatabaseOptions
        { Provider = "Sqlite", ConnectionString = _cs, Seed = true }));
    }

    [Fact]
    public async Task It_reads_the_PLANTS_band_and_not_the_stale_fallback()
    {
        // The first version used QueryFirstOrDefaultAsync<(decimal?, decimal?)>. Dapper does not map
        // columns onto ValueTuple element names, so it returned (null, null) and every caller got the
        // 1.500 fallback — silently, and in the direction that demands an override on trims the plant
        // accepts. This is the test that catches it.
        var band = await _repo.GetEdgeTrimToleranceAsync(CancellationToken.None);
        Assert.Equal(0.75m, band.LowerInches);
        Assert.Equal(12.00m, band.UpperInches);
        Assert.NotEqual(EdgeTrim.LegacyFallback.LowerInches, band.LowerInches);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
