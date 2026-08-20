using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The recovery scrap worksheet — legacy <c>quality/w_recovery.srw</c>, the dw_defects retrieve event
/// (840-945). Three rules with real consequences: the office supersedes the floor, an autopart narrows
/// the defect list, and every configured defect appears whether or not anything is booked against it.
/// </summary>
public sealed class RecoveryWorksheetTests : IClassFixture<RecoveryWorksheetTests.Factory>
{
    private readonly HttpClient _client;
    public RecoveryWorksheetTests(Factory f) => _client = f.Client();

    private Task<JsonElement> Sheet(long job, long coil, long customerId) =>
        _client.GetFromJsonAsync<JsonElement>($"/api/recovery/jobs/{job}/coils/{coil}/worksheet?customerId={customerId}");

    private static (string Code, decimal Wt, int Pc)[] Rows(JsonElement sheet) =>
        sheet.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("scrapCode").GetString() ?? "",
                          r.GetProperty("netWt").GetDecimal(),
                          r.GetProperty("pieces").GetInt32()))
            .ToArray();

    /// <summary>Coil 5003 on job 1002 has office rows for all three defects, so those figures are used
    /// and the source says so.</summary>
    [Fact]
    public async Task Office_figures_are_used_when_the_office_has_booked_any()
    {
        var sheet = await Sheet(1002, 5003, 4001);
        Assert.Equal("office", sheet.GetProperty("source").GetString());

        var rows = Rows(sheet);
        Assert.Equal(250m, rows.Single(r => r.Code == "DENT").Wt);
        Assert.Equal(20, rows.Single(r => r.Code == "DENT").Pc);
        Assert.Equal(150m, rows.Single(r => r.Code == "SCR").Wt);
    }

    /// <summary>Coil 5001 on job 1001 has no office rows, so the worksheet falls back to what the floor
    /// captured — 120 lb / 6 pieces of SCR.</summary>
    [Fact]
    public async Task The_DAS_capture_is_used_when_the_office_has_booked_nothing()
    {
        var sheet = await Sheet(1001, 5001, 4001);
        Assert.Equal("das", sheet.GetProperty("source").GetString());

        var rows = Rows(sheet);
        Assert.Equal(120m, rows.Single(r => r.Code == "SCR").Wt);
        Assert.Equal(6, rows.Single(r => r.Code == "SCR").Pc);
    }

    /// <summary>Every defect the customer tracks appears, carrying zero when nothing is booked against
    /// it. The worksheet is a form to fill in — a defect that is missing cannot be entered.</summary>
    [Fact]
    public async Task Defects_with_nothing_booked_still_appear_at_zero()
    {
        var rows = Rows(await Sheet(1001, 5001, 4001));
        // Customer 4001 tracks DENT + SCR; only SCR has DAS scrap on this coil.
        Assert.Equal(2, rows.Length);
        var dent = rows.Single(r => r.Code == "DENT");
        Assert.Equal(0m, dent.Wt);
        Assert.Equal(0, dent.Pc);
    }

    public sealed class Factory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_recwks_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
            builder.UseSetting("Database:Seed", "true");
            builder.UseSetting("ApiKeys:Enabled", "true");
            builder.UseSetting("ApiKeys:Keys:0", "test-key");
        }

        public HttpClient Client()
        {
            var c = CreateClient();
            c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
            return c;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && File.Exists(_dbPath)) try { File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }
}

/// <summary>
/// The worksheet SAVE rules, in their own class so they get their own fixture database. These tests
/// write, and the read tests above assert on the seeded state — sharing one database would make both
/// sets depend on the order xUnit happens to run them in. Each test here also uses a distinct
/// (job, coil) so they cannot disturb each other either.
/// </summary>
public sealed class RecoveryWorksheetSaveTests : IClassFixture<RecoveryWorksheetSaveTests.Factory>
{
    private readonly HttpClient _client;
    public RecoveryWorksheetSaveTests(Factory f) => _client = f.Client();

    private Task<JsonElement> Sheet(long job, long coil, long customerId) =>
        _client.GetFromJsonAsync<JsonElement>($"/api/recovery/jobs/{job}/coils/{coil}/worksheet?customerId={customerId}");

    private static (string Code, decimal Wt, int Pc)[] Rows(JsonElement sheet) =>
        sheet.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("scrapCode").GetString() ?? "",
                          r.GetProperty("netWt").GetDecimal(),
                          r.GetProperty("pieces").GetInt32()))
            .ToArray();

    /// <summary>
    /// The rule that makes "office" a supersede rather than a merge. Legacy decides with a COUNT before
    /// reading anything, so ONE office row suppresses the DAS numbers for EVERY defect — not just its
    /// own. A merge would silently mix the office's corrected figure for one defect with the floor's
    /// uncorrected figures for the rest.
    /// </summary>
    [Fact]
    public async Task One_office_row_suppresses_the_DAS_numbers_for_every_defect()
    {
        // Job 1001 / coil 5001: DAS has SCR = 120. Book a single office row against DENT instead.
        var save = await _client.PutAsJsonAsync("/api/recovery/jobs/1001/coils/5001/worksheet?customerId=4001", new
        {
            lines = new[] { new { scrapTypeId = 1, netWt = 42, pieces = 3 } },
        });
        Assert.True(save.IsSuccessStatusCode, $"save failed: {save.StatusCode}");

        var sheet = await Sheet(1001, 5001, 4001);
        Assert.Equal("office", sheet.GetProperty("source").GetString());

        var rows = Rows(sheet);
        Assert.Equal(42m, rows.Single(r => r.Code == "DENT").Wt);
        Assert.Equal(0m, rows.Single(r => r.Code == "SCR").Wt);   // the DAS 120 is gone, not merged
    }

    /// <summary>The save asymmetry, which is easy to read as a bug in either direction. A defect NOT yet
    /// booked is only inserted when it carries something — otherwise the table would collect a zero row
    /// for every defect every customer tracks, on every coil.</summary>
    [Fact]
    public async Task An_empty_line_that_was_never_booked_is_not_created()
    {
        var save = await _client.PutAsJsonAsync("/api/recovery/jobs/1003/coils/5004/worksheet?customerId=4001", new
        {
            lines = new[] { new { scrapTypeId = 1, netWt = 0, pieces = 0 } },
        });
        Assert.True(save.IsSuccessStatusCode);
        var sheet = await save.Content.ReadFromJsonAsync<JsonElement>();
        // Nothing was written, so the sheet is still DAS-sourced.
        Assert.Equal("das", sheet.GetProperty("source").GetString());
    }

    /// <summary>...but a defect that IS booked can be corrected down to zero, which is how the office
    /// retracts a wrong figure. And because nothing is deleted, the coil stays office-sourced — the DAS
    /// numbers do not come back. That is legacy's behaviour and it is not reversible from this screen.</summary>
    [Fact]
    public async Task A_booked_line_can_be_zeroed_and_the_coil_stays_office_sourced()
    {
        var url = "/api/recovery/jobs/1002/coils/5003/worksheet?customerId=4001";
        var zeroed = await _client.PutAsJsonAsync(url, new
        {
            lines = new[] { new { scrapTypeId = 1, netWt = 0, pieces = 0 } },
        });
        Assert.True(zeroed.IsSuccessStatusCode);
        var sheet = await zeroed.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0m, Rows(sheet).Single(r => r.Code == "DENT").Wt);
        Assert.Equal("office", sheet.GetProperty("source").GetString());
    }

    public sealed class Factory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_recwks_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
            builder.UseSetting("Database:Seed", "true");
            builder.UseSetting("ApiKeys:Enabled", "true");
            builder.UseSetting("ApiKeys:Keys:0", "test-key");
        }

        public HttpClient Client()
        {
            var c = CreateClient();
            c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
            return c;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && File.Exists(_dbPath)) try { File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }
}
