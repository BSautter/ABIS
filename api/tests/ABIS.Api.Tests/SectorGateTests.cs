using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Abis.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Unit tests for the sector rules themselves — legacy
/// <c>order_entry/w_order_entry.srw:471-580</c>.</summary>
public class SectorRulesTests
{
    [Fact]
    public void A_missing_sector_is_an_error_and_a_present_one_is_not()
    {
        Assert.NotNull(SectorRules.MissingSectorError(null));
        Assert.Null(SectorRules.MissingSectorError(1));
        Assert.Null(SectorRules.MissingSectorError(2));
    }

    /// <summary>The ordering that legacy's if/else forces and that is easy to get wrong: a blank is not
    /// a third sector. An order half filled in would otherwise report a "mix" that is really an
    /// omission, and the operator would be answering the wrong question.</summary>
    [Fact]
    public void Blank_sectors_do_not_count_as_a_distinct_value()
    {
        Assert.False(SectorRules.IsMixed([1, null, 1]));
        Assert.False(SectorRules.IsMixed([null, null]));
        Assert.False(SectorRules.IsMixed([]));
        Assert.False(SectorRules.IsMixed([2]));
        Assert.True(SectorRules.IsMixed([1, 2]));
        Assert.True(SectorRules.IsMixed([1, null, 2]));
    }

    [Fact]
    public void The_prompt_names_the_sectors_involved_by_description_when_known()
    {
        var msg = SectorRules.MixedSectorMessage([1, 2, 1],
            c => c switch { 1 => "Automotive", 2 => "Commercial", _ => null });
        Assert.Contains("Automotive (1)", msg);
        Assert.Contains("Commercial (2)", msg);
        Assert.Contains("confirm=true", msg);
    }

    [Fact]
    public void An_unknown_sector_falls_back_to_the_bare_code()
    {
        Assert.Contains("7", SectorRules.MixedSectorMessage([1, 7], _ => null));
    }
}

/// <summary>End-to-end tests for the sector gate on the order-item write paths.</summary>
public sealed class SectorGateEndpointTests : IClassFixture<SectorGateEndpointTests.Factory>
{
    private readonly HttpClient _client;
    public SectorGateEndpointTests(Factory f) => _client = f.Client();

    private static object Line(string part, int? sector, bool confirm = false) => new
    {
        enduserPartNum = part,
        sheetType = "RECTANGLE",
        trimmingRequired = "N",
        sector,
        confirm,
    };

    private async Task<(HttpStatusCode Status, JsonElement Body, long OrderId)> NewOrder(params object[] items)
    {
        var resp = await _client.PostAsJsonAsync("/api/orders/with-items", new
        {
            order = new { origCustomerId = 4001, origCustomerPo = "PO-SECTOR" },
            items,
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = resp.StatusCode == HttpStatusCode.Created
            ? body.GetProperty("order").GetProperty("orderAbcNum").GetInt64()
            : 0;
        return (resp.StatusCode, body, id);
    }

    [Fact]
    public async Task The_sector_domain_is_exposed_for_the_picker()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/lookups/sectors");
        var codes = body.EnumerateArray().Select(s => s.GetProperty("sectorCode").GetInt32()).ToArray();
        Assert.Equal(new[] { 1, 2 }, codes);
        Assert.Equal("Automotive", body[0].GetProperty("sectorDesc").GetString());
    }

    [Fact]
    public async Task A_line_with_no_sector_is_refused()
    {
        var (status, body, _) = await NewOrder(Line("PN-NOSEC", null));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Sector must be selected", body.ToString());
    }

    /// <summary>Legacy's operator picked from a dropdown and could not type a number. An API caller can,
    /// and <c>order_item.sector</c> has no foreign key to stop it — a bad code would persist silently
    /// and surface later as a blank on a report. Only 1 and 2 exist on the live database.</summary>
    [Fact]
    public async Task A_sector_that_is_not_in_the_domain_is_refused()
    {
        var (status, body, _) = await NewOrder(Line("PN-BADSEC", 7));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Unknown sector 7", body.ToString());
    }

    [Fact]
    public async Task Lines_that_agree_save_without_a_prompt()
    {
        var (status, _, _) = await NewOrder(Line("PN-A", 1), Line("PN-B", 1));
        Assert.Equal(HttpStatusCode.Created, status);
    }

    [Fact]
    public async Task A_mix_of_sectors_is_a_409_that_names_them_and_clears_on_confirm()
    {
        var (status, body, _) = await NewOrder(Line("PN-A", 1), Line("PN-B", 2));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("mixed-sectors", body.GetProperty("code").GetString());
        Assert.Equal(new[] { 1, 2 }, body.GetProperty("sectors").EnumerateArray().Select(s => s.GetInt32()).ToArray());
        Assert.Contains("Automotive", body.GetProperty("message").GetString());

        // Legacy's box defaults to No, so the refusal is the default and Yes has to be said out loud.
        var (confirmed, _, _) = await NewOrder(Line("PN-A", 1), Line("PN-B", 2, confirm: true));
        Assert.Equal(HttpStatusCode.Created, confirmed);
    }

    [Fact]
    public async Task Adding_a_disagreeing_line_to_an_existing_order_is_also_caught()
    {
        var (created, _, orderId) = await NewOrder(Line("PN-A", 1));
        Assert.Equal(HttpStatusCode.Created, created);

        var clash = await _client.PostAsJsonAsync($"/api/orders/{orderId}/items", Line("PN-C", 2));
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal("mixed-sectors",
            (await clash.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var ok = await _client.PostAsJsonAsync($"/api/orders/{orderId}/items", Line("PN-C", 2, confirm: true));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    /// <summary>A PUT is judged on the sector the line is about to have, not the one it is losing.
    /// Without excluding the row being replaced, changing the ONLY line's sector would compare the new
    /// value against its own old value and report a mix that will not exist once the write lands.</summary>
    [Fact]
    public async Task Replacing_a_line_is_judged_on_its_new_sector_not_its_old_one()
    {
        var (created, body, orderId) = await NewOrder(Line("PN-A", 1));
        Assert.Equal(HttpStatusCode.Created, created);
        var itemNum = body.GetProperty("items")[0].GetProperty("orderItemNum").GetInt64();

        var flip = await _client.PutAsJsonAsync($"/api/orders/{orderId}/items/{itemNum}", Line("PN-A", 2));
        Assert.Equal(HttpStatusCode.OK, flip.StatusCode);
    }

    /// <summary>The pre-2017 case. Sector became mandatory in 2017; before that it was blank on 86-98%
    /// of lines, and 38,252 historical orders still carry no sector at all. Editing one of those must
    /// not raise a mix warning just because its other lines predate the rule — the blank lines are an
    /// omission, not a second sector.</summary>
    [Fact]
    public async Task An_order_whose_other_lines_predate_the_rule_does_not_trip_the_mix_warning()
    {
        // Order 9001 is seeded with lines that carry no sector, exactly like a pre-2017 order.
        var resp = await _client.PostAsJsonAsync("/api/orders/9001/items", Line("PN-LEGACY", 2));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    public sealed class Factory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_sector_{Guid.NewGuid():N}.db");
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
