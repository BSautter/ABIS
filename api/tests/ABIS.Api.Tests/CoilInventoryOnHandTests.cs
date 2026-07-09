using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Coil inventory shows only ON-HAND coils by default (legacy w_inv_coil:
/// coil_status NOT IN (0 Done, 10 Shipped, 13 Transferred, 20 Warehouse-item)) — so the count and
/// the weight rollup don't include coils that have left inventory. An explicit status search still
/// overrides the default. The fixture seeds 4 on-hand coils (5001–5004) + 4 excluded (5005–5008).</summary>
public sealed class CoilInventoryOnHandTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_onhand_{Guid.NewGuid():N}.db");
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
    public async Task Default_coil_list_excludes_off_inventory_statuses()
    {
        using var f = new Factory();
        var body = await Client(f).GetFromJsonAsync<JsonElement>("/api/coils?pageSize=100");
        Assert.Equal(4, body.GetProperty("totalCount").GetInt32());   // 5001–5004 on hand; 5005–5008 excluded

        var ids = body.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("coilAbcNum").GetInt64()).ToHashSet();
        Assert.Contains(5001L, ids);
        foreach (var offInventory in new long[] { 5005, 5006, 5007, 5008 })   // Shipped/Transferred/WH/Done
            Assert.DoesNotContain(offInventory, ids);
    }

    [Fact]
    public async Task Explicit_status_search_overrides_the_on_hand_default()
    {
        using var f = new Factory();
        var c = Client(f);
        // The shipped coil (status 10) is hidden by default but retrievable by asking for status 10.
        var shipped = await c.GetFromJsonAsync<JsonElement>("/api/coils?status=10");
        Assert.Equal(1, shipped.GetProperty("totalCount").GetInt32());
        Assert.Equal(5005L, shipped.GetProperty("items")[0].GetProperty("coilAbcNum").GetInt64());
    }

    [Fact]
    public async Task Inventory_summary_counts_only_on_hand()
    {
        using var f = new Factory();
        var groups = await Client(f).GetFromJsonAsync<JsonElement>("/api/coils/summary?groupBy=alloy");
        // On hand: 3003 = {5001,5002}, 5052 = {5003,5004}. The excluded 5005/5007 (3003) and
        // 5006/5008 (5052) must NOT inflate these.
        foreach (var g in groups.EnumerateArray())
            Assert.Equal(2, g.GetProperty("count").GetInt32());
    }
}
