using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Downtime cause-segments (dt_instance_detail): the DAS operator logs downtime WITH a
/// reason. Add a segment to an instance, read it back, and the guards (missing instance / cause).</summary>
public sealed class DowntimeSegmentTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_dtseg_{Guid.NewGuid():N}.db");
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

    [Fact]
    public async Task Add_a_reason_segment_to_a_downtime_instance()
    {
        using var f = new Factory();
        var c = Client(f);

        // A real downtime cause from the seeded master.
        var causes = await c.GetFromJsonAsync<JsonElement>("/api/lookups/downtime-causes");
        var causeId = causes.EnumerateArray().First().GetProperty("id").GetInt64();

        // Create an instance, then log a reason segment against it.
        var inst = await c.PostAsJsonAsync("/api/downtime", new { abJobNum = 1001, lineNum = 110 });
        var instanceNum = (await inst.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("instanceNum").GetInt64();

        var add = await c.PostAsJsonAsync($"/api/downtime/{instanceNum}/segments",
            new { causeId, durationSeconds = 300.0, note = "belt jam" });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        var seg = await add.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(causeId, seg.GetProperty("instanceItem").GetInt64());
        Assert.False(string.IsNullOrWhiteSpace(seg.GetProperty("causeName").GetString()));   // resolved from dt_cause

        // Read it back.
        var list = await c.GetFromJsonAsync<JsonElement>($"/api/downtime/{instanceNum}/segments");
        Assert.Single(list.EnumerateArray());
        Assert.Equal(300.0, list.EnumerateArray().First().GetProperty("duration").GetDouble());
    }

    [Fact]
    public async Task Segment_on_a_missing_instance_is_404()
    {
        using var f = new Factory();
        var resp = await Client(f).PostAsJsonAsync("/api/downtime/99999999/segments", new { causeId = 1, durationSeconds = 60.0 });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Segment_without_a_cause_is_400()
    {
        using var f = new Factory();
        var c = Client(f);
        var inst = await c.PostAsJsonAsync("/api/downtime", new { abJobNum = 1001, lineNum = 110 });
        var instanceNum = (await inst.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("instanceNum").GetInt64();
        var resp = await c.PostAsJsonAsync($"/api/downtime/{instanceNum}/segments", new { durationSeconds = 60.0 });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
