using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// Obsoleting a part and superseding it with a revision — legacy
/// <c>part_num/w_part_num_management.srw</c> (<c>ue_obsolete</c>, <c>ue_create_revision</c>,
/// <c>wf_check_order_item</c>, <c>wf_update_routing_4revision_part</c>).
///
/// <para>These build what they need through the API rather than adding seed rows, so the shared fixture
/// stays exactly as the rest of the suite counts it.</para>
/// </summary>
public sealed class PartLifecycleTests : IClassFixture<PartLifecycleTests.Factory>
{
    private readonly HttpClient _client;
    public PartLifecycleTests(Factory f) => _client = f.Client();

    private async Task<long> NewPart(string partNum = "PN-LIFECYCLE")
    {
        var r = await _client.PostAsJsonAsync("/api/parts", new
        {
            customerId = 4001, enduserPartNum = partNum, sheetType = "RECTANGLE",
            alloy = "3003", temper = "H14", gauge = 0.125, itemStatus = 1,
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("partNumId").GetInt64();
    }

    private async Task AddRouting(long partNumId, long seq, long lineNum = 110, long dieId = 2001)
    {
        var r = await _client.PostAsJsonAsync($"/api/parts/{partNumId}/routings", new
        {
            routingSequence = seq, lineNum, dieId, sheetType = "RECTANGLE",
            spmStandard = 60, spmPlanned = 55, numberOfPeople = 2,
            edgeTrimYN = "N", stackerYN = "Y", efficPercentStandard = 85, efficPercentPlanned = 80,
        });
        Assert.True(r.IsSuccessStatusCode, $"routing {seq} failed: {r.StatusCode}");
    }

    private async Task<int> RoutingCount(long partNumId) =>
        (await _client.GetFromJsonAsync<JsonElement>($"/api/parts/{partNumId}/routings")).GetArrayLength();

    private async Task<int?> ItemStatus(long partNumId) =>
        (await _client.GetFromJsonAsync<JsonElement>($"/api/parts/{partNumId}")).GetProperty("itemStatus").GetInt32();

    // ---- obsolete --------------------------------------------------------------

    [Fact]
    public async Task Obsoleting_retires_the_part()
    {
        var id = await NewPart("PN-OBS-1");
        Assert.Equal(1, await ItemStatus(id));

        var r = await _client.PostAsJsonAsync($"/api/parts/{id}/obsolete", new { });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0, await ItemStatus(id));
    }

    /// <summary>Legacy: "Can't obsolete an non-active part." — it checks the current status first and
    /// refuses before asking anything.</summary>
    [Fact]
    public async Task Obsoleting_an_already_obsolete_part_is_refused()
    {
        var id = await NewPart("PN-OBS-2");
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/api/parts/{id}/obsolete", new { })).StatusCode);

        var again = await _client.PostAsJsonAsync($"/api/parts/{id}/obsolete", new { });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already-obsolete",
            (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Obsoleting_a_part_that_does_not_exist_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PostAsJsonAsync("/api/parts/99999999/obsolete", new { })).StatusCode);
    }

    /// <summary>
    /// The behaviour that reading the function name would get wrong. <c>wf_check_order_item</c> gathers
    /// the order lines still pointing at the part and returns "not OK to obsolete" — but its caller
    /// ignores that verdict: the <c>Return -1</c> is commented out under
    /// <c>//Do not stop processing ... for now</c>. So legacy warns and retires the part anyway, and so
    /// do we. Turning it into a block here would refuse work the plant does today.
    /// </summary>
    [Fact]
    public async Task Open_order_lines_are_a_warning_and_do_NOT_stop_the_retirement()
    {
        var id = await NewPart("PN-OBS-3");
        var line = await _client.PostAsJsonAsync("/api/orders/9001/items", new
        {
            enduserPartNum = "PN-OBS-3", sheetType = "RECTANGLE", trimmingRequired = "N",
            sector = 1, partNumId = id, itemStatus = 2,   // 2 = New: neither Done nor Cancelled
        });
        Assert.Equal(HttpStatusCode.Created, line.StatusCode);

        // The warning is readable before acting, so a UI can show it in the confirmation.
        var warned = await _client.GetFromJsonAsync<JsonElement>($"/api/parts/{id}/order-items");
        Assert.Equal(1, warned.GetArrayLength());
        Assert.Equal("New", warned[0].GetProperty("itemStatusDesc").GetString());

        var r = await _client.PostAsJsonAsync($"/api/parts/{id}/obsolete", new { });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("blockingOrderItems").GetArrayLength());
        Assert.Equal(0, await ItemStatus(id));      // retired regardless — the point of this test
    }

    /// <summary>Done (0) and Cancelled (3) are precisely the two statuses legacy treats as safe.</summary>
    [Fact]
    public async Task A_done_order_line_is_not_reported()
    {
        var id = await NewPart("PN-OBS-4");
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/orders/9001/items", new
        {
            enduserPartNum = "PN-OBS-4", sheetType = "RECTANGLE", trimmingRequired = "N",
            sector = 1, partNumId = id, itemStatus = 0,   // Done
        })).StatusCode);

        var warned = await _client.GetFromJsonAsync<JsonElement>($"/api/parts/{id}/order-items");
        Assert.Equal(0, warned.GetArrayLength());
    }

    // ---- revise ----------------------------------------------------------------

    /// <summary>The revision is ACTIVE even though the part it came from was just retired — and it has
    /// to be set explicitly, because item_status rides in the copied columns and would otherwise be
    /// inherited as 0.</summary>
    [Fact]
    public async Task A_revision_is_a_new_active_part_even_when_the_source_was_just_retired()
    {
        var id = await NewPart("PN-REV-1");
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/api/parts/{id}/obsolete", new { })).StatusCode);
        Assert.Equal(0, await ItemStatus(id));

        var r = await _client.PostAsJsonAsync($"/api/parts/{id}/revise", new { moveRouting = false });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        var newId = body.GetProperty("part").GetProperty("partNumId").GetInt64();

        Assert.NotEqual(id, newId);
        Assert.Equal(id, body.GetProperty("previousPartNumId").GetInt64());
        Assert.Equal(1, await ItemStatus(newId));
        Assert.Equal(0, await ItemStatus(id));      // the source stays retired
        Assert.Equal("PN-REV-1", body.GetProperty("part").GetProperty("enduserPartNum").GetString());
    }

    [Fact]
    public async Task A_revision_carries_the_blank_geometry()
    {
        // Seed part 6001 is the one with RECTANGLE dimensions in the fixture.
        var r = await _client.PostAsJsonAsync("/api/parts/6001/revise", new { moveRouting = false });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var newId = (await r.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("part").GetProperty("partNumId").GetInt64();

        var shape = await _client.GetFromJsonAsync<JsonElement>($"/api/parts/{newId}/shape");
        var src = await _client.GetFromJsonAsync<JsonElement>("/api/parts/6001/shape");
        Assert.Equal(src.ToString(), shape.ToString().Replace(newId.ToString(), "6001"));
    }

    /// <summary>
    /// The one place a revision differs from a duplicate, and the reason they cannot share code however
    /// alike they look: legacy's <c>wf_update_routing_4revision_part</c> is an
    /// <c>UPDATE routing SET part_num_id = new</c>. The routing LEAVES the old part. That is right for a
    /// revision — there is one real routing and the successor inherits it — and wrong for
    /// <c>/copy</c>, where both parts stay live and each needs its own.
    /// </summary>
    [Fact]
    public async Task Routings_MOVE_on_a_revision_where_copy_would_duplicate_them()
    {
        var id = await NewPart("PN-REV-2");
        await AddRouting(id, 1);
        Assert.Equal(1, await RoutingCount(id));

        var r = await _client.PostAsJsonAsync($"/api/parts/{id}/revise", new { moveRouting = true });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        var newId = body.GetProperty("part").GetProperty("partNumId").GetInt64();

        Assert.Equal(1, body.GetProperty("movedRoutingSequence").GetInt64());
        Assert.Equal(1, await RoutingCount(newId));
        Assert.Equal(0, await RoutingCount(id));    // MOVED, not copied

        // Contrast, in the same test so the difference cannot drift apart unnoticed: /copy duplicates.
        var copied = await _client.PostAsync($"/api/parts/{newId}/copy", null);
        Assert.Equal(HttpStatusCode.Created, copied.StatusCode);
        var copyId = (await copied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("partNumId").GetInt64();
        Assert.Equal(1, await RoutingCount(copyId));
        Assert.Equal(1, await RoutingCount(newId));  // the source KEEPS its routing
    }

    [Fact]
    public async Task Without_moveRouting_the_routing_stays_on_the_old_part()
    {
        var id = await NewPart("PN-REV-3");
        await AddRouting(id, 1);

        var r = await _client.PostAsJsonAsync($"/api/parts/{id}/revise", new { moveRouting = false });
        var newId = (await r.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("part").GetProperty("partNumId").GetInt64();

        Assert.Equal(1, await RoutingCount(id));
        Assert.Equal(0, await RoutingCount(newId));
    }

    /// <summary>Legacy moves a lone routing without asking which, and opens
    /// <c>w_routing_4customer_and_part</c> when there are several. The 409 is that window.</summary>
    [Fact]
    public async Task Several_routings_ask_which_one_to_move()
    {
        var id = await NewPart("PN-REV-4");
        await AddRouting(id, 1);
        await AddRouting(id, 2, lineNum: 120, dieId: 2002);   // 110/120 are the fixture's two real lines

        var ask = await _client.PostAsJsonAsync($"/api/parts/{id}/revise", new { moveRouting = true });
        Assert.Equal(HttpStatusCode.Conflict, ask.StatusCode);
        var body = await ask.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("routing-choice-required", body.GetProperty("code").GetString());
        Assert.Equal(2, body.GetProperty("routings").GetArrayLength());
        Assert.Equal(2, await RoutingCount(id));    // nothing moved while it was asking

        var chosen = await _client.PostAsJsonAsync($"/api/parts/{id}/revise", new { moveRouting = true, routingSequence = 2 });
        Assert.Equal(HttpStatusCode.Created, chosen.StatusCode);
        var newId = (await chosen.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("part").GetProperty("partNumId").GetInt64();

        Assert.Equal(1, await RoutingCount(newId));   // only the named one
        Assert.Equal(1, await RoutingCount(id));
    }

    [Fact]
    public async Task Naming_a_routing_the_part_does_not_have_asks_again_rather_than_moving_nothing()
    {
        var id = await NewPart("PN-REV-5");
        await AddRouting(id, 1);

        var r = await _client.PostAsJsonAsync($"/api/parts/{id}/revise", new { moveRouting = true, routingSequence = 99 });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(1, await RoutingCount(id));
    }

    [Fact]
    public async Task Revising_a_part_that_does_not_exist_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PostAsJsonAsync("/api/parts/99999999/revise", new { moveRouting = false })).StatusCode);
    }

    public sealed class Factory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_partlife_{Guid.NewGuid():N}.db");
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
