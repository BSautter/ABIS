using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Abis.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Coil scan-to-load at the DAS station (legacy <c>w_scan_coil_id</c>): normalising the
/// scanned label, resolving it against the coils on the job, and recording the coil's actual weighed
/// weight under the legacy plausibility guard.</summary>
public sealed class CoilScanTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_coilscan_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
            builder.UseSetting("Database:Seed", "true");
            builder.UseSetting("ApiKeys:Enabled", "true");
            builder.UseSetting("ApiKeys:Keys:0", "test-key");
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }

    private static HttpClient Client(WebApplicationFactory<Program> f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return c;
    }

    // ---- The label rules, unit-tested with no database (legacy w_scan_coil_id normalisation) ----

    [Theory]
    [InlineData("5001", "5001", false)]              // a plain coil number
    [InlineData("  5001  ", "5001", false)]          // trimmed
    [InlineData("2S5001", "5001", true)]             // the vendor header is stripped
    [InlineData("ABC2S5001", "5001", true)]          // …along with everything before it
    [InlineData("2s5001", "5001", true)]             // scanners may emit lower case
    public void Barcodes_normalise_the_way_legacy_does(string raw, string expected, bool stripped)
    {
        var (normalized, headerStripped, valid) = CoilBarcode.Parse(raw);
        Assert.Equal(expected, normalized);
        Assert.Equal(stripped, headerStripped);
        Assert.True(valid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABC")]        // letters only — a mis-read, not an id
    [InlineData("50-01")]      // punctuation from a bad scan
    [InlineData("2S")]         // header with nothing after it
    public void Unreadable_barcodes_are_rejected_not_guessed(string? raw)
        => Assert.False(CoilBarcode.Parse(raw).Valid);

    // ---- The job-scoped lookup over HTTP ----

    [Fact]
    public async Task A_scanned_coil_on_the_job_resolves_with_its_identity_and_weight()
    {
        using var f = new Factory();
        var c = Client(f);

        // Coil 5001 is on job 1001 in the fixture. Scan it WITH the vendor header to prove the
        // strip happens server-side, so every scanning surface gets the same rule.
        var r = await c.GetFromJsonAsync<JsonElement>("/api/das/scan/coil?barcode=2S5001&abJobNum=1001");
        Assert.Equal("Resolved", r.GetProperty("outcome").GetString());
        Assert.Equal("5001", r.GetProperty("normalized").GetString());
        Assert.True(r.GetProperty("headerStripped").GetBoolean());
        Assert.Equal(5001, r.GetProperty("coilAbcNum").GetInt64());
        Assert.False(string.IsNullOrWhiteSpace(r.GetProperty("coilOrgNum").GetString()));
        // The operator confirms against the weight left on the coil.
        Assert.Equal(8000m, r.GetProperty("netWtBalance").GetDecimal());
    }

    [Fact]
    public async Task A_coil_that_is_not_on_the_job_is_refused_with_a_reason()
    {
        using var f = new Factory();
        var c = Client(f);

        // 5001 exists but belongs to job 1001 — scanning it while working 1002 must NOT load it.
        var r = await c.GetFromJsonAsync<JsonElement>("/api/das/scan/coil?barcode=5001&abJobNum=1002");
        Assert.Equal("NotOnJob", r.GetProperty("outcome").GetString());
        Assert.Contains("not on job", r.GetProperty("reason").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JsonValueKind.Null, r.GetProperty("coilAbcNum").ValueKind);
    }

    [Fact]
    public async Task An_unreadable_scan_reports_unreadable_rather_than_looking_anything_up()
    {
        using var f = new Factory();
        var c = Client(f);

        var r = await c.GetFromJsonAsync<JsonElement>("/api/das/scan/coil?barcode=XX-YY&abJobNum=1001");
        Assert.Equal("Unreadable", r.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("coilAbcNum").ValueKind);

        // A missing barcode / unknown job are caller errors, not scan outcomes.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/das/scan/coil?barcode=&abJobNum=1001")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/das/scan/coil?barcode=5001&abJobNum=999999")).StatusCode);
    }

    // ---- The actual-weight write + its legacy plausibility guard ----

    [Fact]
    public async Task Actual_weight_is_recorded_on_the_coil()
    {
        using var f = new Factory();
        var c = Client(f);

        var resp = await c.PostAsJsonAsync("/api/coils/5001/actual-weight", new { weight = 11850 });
        resp.EnsureSuccessStatusCode();
        Assert.Equal(11850m, (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("abcoCoilNetWt").GetDecimal());

        // It reads back on the coil, and on a later scan of the same coil.
        var coil = await c.GetFromJsonAsync<JsonElement>("/api/coils/5001");
        Assert.Equal(11850m, coil.GetProperty("abcoCoilNetWt").GetDecimal());
        var scan = await c.GetFromJsonAsync<JsonElement>("/api/das/scan/coil?barcode=5001&abJobNum=1001");
        Assert.Equal(11850m, scan.GetProperty("abcoCoilNetWt").GetDecimal());
    }

    [Theory]
    [InlineData(100)]      // the bound itself is excluded (legacy: > 100)
    [InlineData(5)]        // a scale misread
    [InlineData(99999)]    // legacy: < 99999
    [InlineData(1000000)]  // a slipped digit
    public async Task An_implausible_actual_weight_is_refused(int weight)
    {
        using var f = new Factory();
        var c = Client(f);

        var resp = await c.PostAsJsonAsync("/api/coils/5001/actual-weight", new { weight });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        // …and nothing was written.
        var coil = await c.GetFromJsonAsync<JsonElement>("/api/coils/5001");
        Assert.Equal(JsonValueKind.Null, coil.GetProperty("abcoCoilNetWt").ValueKind);
    }

    [Fact]
    public async Task An_unknown_coil_404s()
        => Assert.Equal(HttpStatusCode.NotFound,
            (await Client(new Factory()).PostAsJsonAsync("/api/coils/999999/actual-weight", new { weight = 5000 })).StatusCode);
}
