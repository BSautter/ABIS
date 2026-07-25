using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>The plant-maintained PLC fault-code dictionary. ABIS ships it EMPTY on purpose — a line's
/// <c>activefault</c> code is defined by that line's PLC program, there is no mapping in the ABIS
/// schema, and legacy never decoded it (it only tested &gt; 0). These tests pin that we provide the
/// place to record meanings without inventing any.</summary>
public sealed class PlcFaultCodeTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_faultcode_{Guid.NewGuid():N}.db");
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
    public async Task The_dictionary_ships_empty_because_we_do_not_invent_meanings()
    {
        using var f = new Factory();
        var c = Client(f);

        var all = await c.GetFromJsonAsync<JsonElement>("/api/lookups/plc-fault-codes");
        Assert.Empty(all.EnumerateArray());
    }

    [Fact]
    public async Task A_recorded_code_reads_back_for_its_line()
    {
        using var f = new Factory();
        var c = Client(f);

        // 68 is a code seen live on BL110 — the meaning here is the plant's to supply, not ours.
        var put = await c.PutAsJsonAsync("/api/lookups/plc-fault-codes/6/68", new { description = "Feed jam", notes = "clear the feed table" });
        put.EnsureSuccessStatusCode();
        var saved = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Feed jam", saved.GetProperty("description").GetString());
        Assert.Equal(68, saved.GetProperty("faultCode").GetInt64());

        var forLine = await c.GetFromJsonAsync<JsonElement>("/api/lookups/plc-fault-codes?lineNum=6");
        Assert.Contains(forLine.EnumerateArray(), e => e.GetProperty("faultCode").GetInt64() == 68);
    }

    [Fact]
    public async Task Line_zero_is_a_wildcard_that_applies_to_every_line()
    {
        using var f = new Factory();
        var c = Client(f);

        (await c.PutAsJsonAsync("/api/lookups/plc-fault-codes/0/99", new { description = "E-stop pressed" })).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/lookups/plc-fault-codes/6/68", new { description = "Feed jam" })).EnsureSuccessStatusCode();

        // A line's view includes its own codes AND the wildcards — a code that means the same
        // everywhere shouldn't have to be entered once per line.
        var line6 = await c.GetFromJsonAsync<JsonElement>("/api/lookups/plc-fault-codes?lineNum=6");
        var codes = line6.EnumerateArray().Select(e => e.GetProperty("faultCode").GetInt64()).ToList();
        Assert.Contains(99L, codes);
        Assert.Contains(68L, codes);

        // …but another line sees only the wildcard, not line 6's private code.
        var line7 = await c.GetFromJsonAsync<JsonElement>("/api/lookups/plc-fault-codes?lineNum=7");
        var codes7 = line7.EnumerateArray().Select(e => e.GetProperty("faultCode").GetInt64()).ToList();
        Assert.Contains(99L, codes7);
        Assert.DoesNotContain(68L, codes7);
    }

    [Fact]
    public async Task Recording_the_same_code_again_corrects_it_rather_than_duplicating()
    {
        using var f = new Factory();
        var c = Client(f);

        (await c.PutAsJsonAsync("/api/lookups/plc-fault-codes/6/68", new { description = "Feed jam" })).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/lookups/plc-fault-codes/6/68", new { description = "Feed jam at leveller" })).EnsureSuccessStatusCode();

        var forLine = await c.GetFromJsonAsync<JsonElement>("/api/lookups/plc-fault-codes?lineNum=6");
        var matches = forLine.EnumerateArray().Where(e => e.GetProperty("faultCode").GetInt64() == 68).ToList();
        Assert.Single(matches);
        Assert.Equal("Feed jam at leveller", matches[0].GetProperty("description").GetString());
    }

    [Fact]
    public async Task A_meaning_is_required_and_an_entry_can_be_removed()
    {
        using var f = new Factory();
        var c = Client(f);

        // An empty description would be worse than no entry — the lamp would decode to nothing.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await c.PutAsJsonAsync("/api/lookups/plc-fault-codes/6/70", new { description = "   " })).StatusCode);

        (await c.PutAsJsonAsync("/api/lookups/plc-fault-codes/6/70", new { description = "Guard door open" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/lookups/plc-fault-codes/6/70")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/lookups/plc-fault-codes/6/70")).StatusCode);
    }
}
