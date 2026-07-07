using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>End-to-end HTTP tests that boot the real app (minimal-API pipeline,
/// DI, JSON serialization) against a unique seeded SQLite fixture.</summary>
public sealed class ApiSmokeTests : IClassFixture<ApiSmokeTests.ApiFactory>
{
    private readonly HttpClient _client;
    private readonly ApiFactory _factory;

    public ApiSmokeTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
    }

    [Fact]
    public async Task Health_is_ok()
    {
        var resp = await _client.GetAsync("/health");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Responses_carry_baseline_security_headers()
    {
        var resp = await _client.GetAsync("/health");
        Assert.Equal("nosniff", resp.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", resp.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Response_carries_a_generated_request_id()
    {
        var resp = await _client.GetAsync("/health");
        Assert.True(resp.Headers.Contains("X-Request-Id"));
        Assert.False(string.IsNullOrWhiteSpace(resp.Headers.GetValues("X-Request-Id").Single()));
    }

    [Fact]
    public async Task OrderItem_shape_geometry_round_trips_over_http()
    {
        // Seeded line 7001 is a RECTANGLE.
        var get = await _client.GetFromJsonAsync<JsonElement>("/api/orders/9001/items/7001/shape");
        Assert.Equal("RECTANGLE", get.GetProperty("shapeType").GetString());
        Assert.Contains(get.GetProperty("dimensions").EnumerateArray(), d => d.GetProperty("name").GetString() == "length");

        // PUT circle geometry onto line 7002.
        var put = await _client.PutAsJsonAsync("/api/orders/9001/items/7002/shape", new
        {
            shapeType = "CIRCLE",
            dimensions = new[] { new { name = "diameter", value = 30.0, plusTol = 0.2, minusTol = 0.2 } },
            dies = new[] { "DIE-HTTP" },
        });
        put.EnsureSuccessStatusCode();
        var saved = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CIRCLE", saved.GetProperty("shapeType").GetString());

        // Unknown shape -> 400.
        var bad = await _client.PutAsJsonAsync("/api/orders/9001/items/7002/shape",
            new { shapeType = "NOPE", dimensions = Array.Empty<object>(), dies = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // Missing line -> 404.
        var missing = await _client.GetAsync("/api/orders/9001/items/9999/shape");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // Catalog lists all 10 shapes.
        var types = await _client.GetFromJsonAsync<JsonElement>("/api/lookups/shape-types");
        Assert.Equal(10, types.GetArrayLength());
    }

    [Fact]
    public async Task Part_shape_geometry_round_trips_over_http()
    {
        // Seed part 6001 is a RECTANGLE.
        var get = await _client.GetFromJsonAsync<JsonElement>("/api/parts/6001/shape");
        Assert.Equal("RECTANGLE", get.GetProperty("shapeType").GetString());

        var put = await _client.PutAsJsonAsync("/api/parts/6002/shape", new
        {
            shapeType = "CIRCLE",
            dimensions = new[] { new { name = "diameter", value = 18.0, plusTol = 0.1, minusTol = 0.1 } },
        });
        put.EnsureSuccessStatusCode();
        var saved = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CIRCLE", saved.GetProperty("shapeType").GetString());

        var missing = await _client.GetAsync("/api/parts/999999/shape");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Skid_tag_documents_render_printable_html_with_barcode()
    {
        var resp = await _client.GetAsync("/api/documents/sheet-skid/3001");
        resp.EnsureSuccessStatusCode();
        Assert.Equal("text/html", resp.Content.Headers.ContentType!.MediaType);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("SHEET SKID TAG", html);
        Assert.Contains("3001", html);
        Assert.Contains("<svg", html);      // Code 39 barcode present
        Assert.Contains("<rect", html);     // ...with rendered bars

        var scrap = await _client.GetAsync("/api/documents/scrap-skid/8001");
        scrap.EnsureSuccessStatusCode();
        Assert.Contains("SCRAP SKID TAG", await scrap.Content.ReadAsStringAsync());

        var coil = await _client.GetAsync("/api/documents/coil-label/5001");
        coil.EnsureSuccessStatusCode();
        var coilHtml = await coil.Content.ReadAsStringAsync();
        Assert.Contains("COIL ABC LABEL", coilHtml);
        Assert.Contains("5001", coilHtml);

        var noDoc = await _client.GetAsync("/api/documents/sheet-skid/999999");
        Assert.Equal(HttpStatusCode.NotFound, noDoc.StatusCode);
    }

    [Fact]
    public async Task Order_item_edge_trim_tolerance_is_enforced()
    {
        static object Item(double? inc, double? trm) => new
        {
            enduserPartNum = "PN-TRIM", sheetType = "RECTANGLE",
            trimmingRequired = "Y", incomingCoilWidth = inc, trimmedCoilWidth = trm, trimTypeCode = 1,
        };
        // Under tolerance (0.1" < 1.5"), over tolerance (13" > 12"), and incoming < trimmed -> 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/orders/9001/items", Item(48.0, 47.9))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/orders/9001/items", Item(60.0, 47.0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/orders/9001/items", Item(40.0, 45.0))).StatusCode);
        // Trimming required but widths missing -> 400.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/orders/9001/items", new { enduserPartNum = "PN-TRIM", sheetType = "RECTANGLE", trimmingRequired = "Y" })).StatusCode);
        // Valid trim (2.0" within tolerance) -> 201; trimming not required -> widths irrelevant -> 201.
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/orders/9001/items", Item(48.0, 46.0))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/orders/9001/items", new { enduserPartNum = "PN-NOTRIM", sheetType = "RECTANGLE", trimmingRequired = "N" })).StatusCode);
        // Out-of-tolerance is OVERRIDABLE: override flag without a user -> 400; with a user -> 201.
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/orders/9001/items",
            new { enduserPartNum = "PN-TRIM", sheetType = "RECTANGLE", trimmingRequired = "Y", incomingCoilWidth = 60.0, trimmedCoilWidth = 47.0, trimTypeCode = 1, trimmedWidthOverridden = "Y" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/orders/9001/items",
            new { enduserPartNum = "PN-TRIM", sheetType = "RECTANGLE", trimmingRequired = "Y", incomingCoilWidth = 60.0, trimmedCoilWidth = 47.0, trimTypeCode = 1, trimmedWidthOverridden = "Y", trimmedWidthOverrideUser = "qa" })).StatusCode);
    }

    [Fact]
    public async Task A_supplied_request_id_is_echoed()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/health");
        req.Headers.Add("X-Request-Id", "trace-echo-1");
        var resp = await _client.SendAsync(req);
        Assert.Equal("trace-echo-1", resp.Headers.GetValues("X-Request-Id").Single());
    }

    [Fact]
    public async Task Request_id_is_recorded_in_the_audit_trail()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        client.DefaultRequestHeaders.Add("X-Request-Id", "trace-audit-xyz");
        await client.PostAsJsonAsync("/api/customers", new { customerName = "TRACE CO" });

        var log = await client.GetFromJsonAsync<JsonElement>("/api/audit-log?source=customers&pageSize=100");
        var found = log.GetProperty("items").EnumerateArray()
            .Any(i => (i.GetProperty("notes").GetString() ?? "").Contains("trace-audit-xyz"));
        Assert.True(found);
    }

    [Fact]
    public async Task Api_request_without_key_is_401()
    {
        var bare = _factory.CreateClient();   // no X-Api-Key header
        var resp = await bare.GetAsync("/api/jobs");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Api_request_with_bad_key_is_401()
    {
        var bare = _factory.CreateClient();
        bare.DefaultRequestHeaders.Add("X-Api-Key", "wrong");
        var resp = await bare.GetAsync("/api/jobs");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Health_is_anonymous()
    {
        var bare = _factory.CreateClient();   // no key
        var resp = await bare.GetAsync("/health");
        resp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Root_returns_service_info_anonymously()
    {
        var bare = _factory.CreateClient();   // no key
        var body = await bare.GetFromJsonAsync<JsonElement>("/");
        Assert.Equal("ABIS API", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Order_entry_demo_page_is_served()
    {
        var bare = _factory.CreateClient();   // static files are anonymous
        var resp = await bare.GetAsync("/ui/index.html");
        resp.EnsureSuccessStatusCode();
        Assert.Contains("ABIS Order Entry", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Coil_inventory_demo_page_is_served()
    {
        var bare = _factory.CreateClient();
        var resp = await bare.GetAsync("/ui/coils.html");
        resp.EnsureSuccessStatusCode();
        Assert.Contains("ABIS Coil Inventory", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Qa_demo_page_is_served()
    {
        var bare = _factory.CreateClient();
        var resp = await bare.GetAsync("/ui/qa.html");
        resp.EnsureSuccessStatusCode();
        Assert.Contains("ABIS QA Test Results", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Typed_client_demo_page_is_served()
    {
        var bare = _factory.CreateClient();
        var resp = await bare.GetAsync("/ui/typed.html");
        resp.EnsureSuccessStatusCode();
        Assert.Contains("Typed Client", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Generated_client_es_module_is_served()
    {
        // The committed, compiled NSwag client (tsc output) ships as a browser
        // ES module so the typed demo runs with no runtime build step.
        var bare = _factory.CreateClient();
        var resp = await bare.GetAsync("/ui/app/generated/abis-client.js");
        resp.EnsureSuccessStatusCode();
        Assert.Contains("class AbisClient", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task List_jobs_returns_paged_envelope()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/jobs");
        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Get_job_returns_entity()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/jobs/1001");
        Assert.Equal(1001, body.GetProperty("abJobNum").GetInt64());
    }

    [Fact]
    public async Task Get_unknown_job_is_404()
    {
        var resp = await _client.GetAsync("/api/jobs/999999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Get_job_coils_returns_two()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/jobs/1001/coils");
        Assert.Equal(2, body.GetArrayLength());
    }

    [Fact]
    public async Task Coils_status_filter_applies()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/coils?status=3");
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task List_customers_returns_seeded()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/customers");
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 2);
    }

    [Fact]
    public async Task Get_job_skids_returns_two()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/jobs/1001/skids");
        Assert.Equal(2, body.GetArrayLength());
    }

    [Fact]
    public async Task Create_customer_returns_201_and_is_retrievable()
    {
        var resp = await _client.PostAsJsonAsync("/api/customers", new { customerName = "DELTA EXTRUSIONS", customerShortName = "DELTA" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var created = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("customerId").GetInt64();
        var fetched = await _client.GetFromJsonAsync<JsonElement>($"/api/customers/{id}");
        Assert.Equal("DELTA EXTRUSIONS", fetched.GetProperty("customerName").GetString());
    }

    [Fact]
    public async Task Create_customer_without_name_returns_400()
    {
        var resp = await _client.PostAsJsonAsync("/api/customers", new { customerShortName = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_sketch_with_overlong_name_returns_400()
    {
        // sketch_name is VARCHAR2(16); a longer value must fail as 400, not a DB 500.
        var resp = await _client.PostAsJsonAsync("/api/sketches", new { sketchName = new string('Z', 17) });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task IfMatch_optimistic_concurrency_on_write()
    {
        // Create a die, then GET it to obtain its content ETag.
        var created = await _client.PostAsJsonAsync("/api/dies", new { dieName = "IFMATCH" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("dieId").GetInt64();
        var get = await _client.GetAsync($"/api/dies/{id}");
        var etag = get.Headers.ETag!.ToString();
        Assert.StartsWith("W/\"", etag);

        // A matching If-Match passes — this also proves the write-side hash matches
        // the GET response's hash byte-for-byte.
        var ok = new HttpRequestMessage(HttpMethod.Put, $"/api/dies/{id}")
        { Content = JsonContent.Create(new { dieName = "IFMATCH", status = 1 }) };
        ok.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(ok)).StatusCode);

        // A stale validator is rejected with 412 (the row changed since `etag`).
        var stale = new HttpRequestMessage(HttpMethod.Put, $"/api/dies/{id}")
        { Content = JsonContent.Create(new { dieName = "IFMATCH", status = 2 }) };
        stale.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await _client.SendAsync(stale)).StatusCode);

        // No If-Match → the precondition is optional, so the write proceeds.
        var noPrecond = await _client.PutAsJsonAsync($"/api/dies/{id}", new { dieName = "IFMATCH", status = 3 });
        Assert.Equal(HttpStatusCode.OK, noPrecond.StatusCode);
    }

    [Fact]
    public async Task Create_order_without_po_returns_400()
    {
        // orig_customer_po is NOT NULL; the newly-guarded endpoint must reject the omission.
        var resp = await _client.PostAsJsonAsync("/api/orders", new { origCustomerId = 4001 });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Patch_job_updates_status()
    {
        var resp = await _client.PatchAsJsonAsync("/api/jobs/1002", new { jobStatus = 7 });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(7, body.GetProperty("jobStatus").GetInt32());
    }

    [Fact]
    public async Task Patch_unknown_job_returns_404()
    {
        var resp = await _client.PatchAsJsonAsync("/api/jobs/999999", new { jobStatus = 1 });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_order_returns_201()
    {
        var resp = await _client.PostAsJsonAsync("/api/orders", new { origCustomerId = 4001, origCustomerPo = "PO-HTTP" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
    }

    [Fact]
    public async Task Create_order_item_without_part_returns_400()
    {
        var resp = await _client.PostAsJsonAsync("/api/orders/9001/items", new { alloy2 = "3003" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Mutating_requests_are_audited()
    {
        // A write...
        await _client.PostAsJsonAsync("/api/customers", new { customerName = "AUDITED CO" });
        // ...produces an audit-log entry for that route.
        var log = await _client.GetFromJsonAsync<JsonElement>("/api/audit-log?source=customers");
        Assert.True(log.GetProperty("totalCount").GetInt32() >= 1);
        var first = log.GetProperty("items")[0];
        Assert.Contains("/api/customers", first.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Create_coil_returns_201_and_is_retrievable()
    {
        var resp = await _client.PostAsJsonAsync("/api/coils", new { coilAlloy2 = "6061", coilGauge = 0.25, netWt = 15000 });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
        var created = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("coilAbcNum").GetInt64();
        var fetched = await _client.GetFromJsonAsync<JsonElement>($"/api/coils/{id}");
        Assert.Equal("6061", fetched.GetProperty("coilAlloy2").GetString());
    }

    [Fact]
    public async Task Create_coil_without_alloy_returns_400()
    {
        var resp = await _client.PostAsJsonAsync("/api/coils", new { coilGauge = 0.1 });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_sheet_skid_returns_201()
    {
        var resp = await _client.PostAsJsonAsync("/api/sheet-skids", new { abJobNum = 1001, sheetNetWt = 2000, skidPieces = 100 });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Order_full_returns_header_customer_and_items()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/orders/9001/full");
        Assert.Equal(9001, body.GetProperty("order").GetProperty("orderAbcNum").GetInt64());
        Assert.Equal(4001, body.GetProperty("customer").GetProperty("customerId").GetInt64());
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Create_order_with_items_returns_201_with_lines()
    {
        var resp = await _client.PostAsJsonAsync("/api/orders/with-items", new
        {
            order = new { origCustomerId = 4001, origCustomerPo = "PO-HTTP-COMBO" },
            items = new[]
            {
                new { enduserPartNum = "PN-X", alloy2 = "3003", sheetType = "FLAT" },
                new { enduserPartNum = "PN-Y", alloy2 = "5052", sheetType = "FLAT" }
            }
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var detail = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, detail.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Create_order_with_invalid_item_returns_400()
    {
        var resp = await _client.PostAsJsonAsync("/api/orders/with-items", new
        {
            order = new { origCustomerId = 4001 },
            items = new[] { new { alloy2 = "3003" } }   // missing enduserPartNum
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Lookups_alloys_contains_seeded_values()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/lookups/alloys");
        var alloys = body.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("3003", alloys);
    }

    [Fact]
    public async Task List_edi_transactions_newest_first()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/edi/transactions");
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 2);
        var items = body.GetProperty("items");
        // default order is edi_file_id DESC
        Assert.Equal(9002, items[0].GetProperty("ediFileId").GetInt64());
        Assert.Equal("870", items[0].GetProperty("transactionTypeId").GetString());
    }

    [Fact]
    public async Task Get_edi_transaction_by_id()
    {
        var tx = await _client.GetFromJsonAsync<JsonElement>("/api/edi/transactions/9001");
        Assert.Equal("856", tx.GetProperty("transactionTypeId").GetString());
        Assert.Equal(4001, tx.GetProperty("customerId").GetInt64());
    }

    [Fact]
    public async Task Edi_transactions_filter_by_type()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/edi/transactions?transactionTypeId=856");
        var types = body.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("transactionTypeId").GetString()).Distinct().ToList();
        Assert.Equal(new[] { "856" }, types);
    }

    [Fact]
    public async Task List_edi_log_paged()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/edi/log");
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 2);
    }

    [Fact]
    public async Task Lookups_edi_types_contains_seeded()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/lookups/edi-types");
        var ids = body.EnumerateArray().Select(e => e.GetProperty("ediTypeId").GetInt32()).ToList();
        Assert.Contains(856, ids);
        Assert.Contains(870, ids);
    }

    [Fact]
    public async Task Coils_filter_by_alloy()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/coils?alloy=3003");
        Assert.Equal(2, body.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Coil_inventory_summary_by_alloy()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/coils/summary?groupBy=alloy");
        var keys = body.EnumerateArray().Select(g => g.GetProperty("key").GetString()).ToList();
        Assert.Contains("3003", keys);
        Assert.Contains("5052", keys);
    }

    [Fact]
    public async Task Coil_inventory_summary_bad_groupby_is_400()
    {
        var resp = await _client.GetAsync("/api/coils/summary?groupBy=bogus");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Coil_processing_history_is_served()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/coils/5001/processing");
        Assert.Equal(1, body.GetArrayLength());
    }

    [Fact]
    public async Task List_jobs_sorted_by_status_desc_applies()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/jobs?sort=jobStatus&dir=desc");
        var statuses = body.GetProperty("items").EnumerateArray()
            .Select(j => j.GetProperty("jobStatus").GetInt32()).ToList();
        Assert.Equal(statuses.OrderByDescending(s => s).ToList(), statuses);
    }

    [Fact]
    public async Task List_with_unknown_sort_field_is_400()
    {
        var resp = await _client.GetAsync("/api/jobs?sort=bogusColumn");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task List_with_bad_direction_is_400()
    {
        var resp = await _client.GetAsync("/api/coils?sort=netWt&dir=upward");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Readiness_probe_reports_ready_against_fixture()
    {
        var resp = await _client.GetAsync("/health/ready");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Readiness_probe_is_anonymous()
    {
        var bare = _factory.CreateClient();   // no key
        var resp = await bare.GetAsync("/health/ready");
        resp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Test_results_filter_by_position()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/test-results?position=M");
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Temp_test_results_are_listed()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/temp-test-results");
        Assert.Equal(2, body.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Partial_skids_list_and_by_job()
    {
        var all = await _client.GetFromJsonAsync<JsonElement>("/api/partial-skids");
        Assert.Equal(3, all.GetProperty("totalCount").GetInt32());

        var byJob = await _client.GetFromJsonAsync<JsonElement>("/api/jobs/1001/partial-skids");
        Assert.Equal(2, byJob.GetArrayLength());
    }

    [Fact]
    public async Task Get_returns_an_etag_and_honors_if_none_match()
    {
        var first = await _client.GetAsync("/api/jobs/1001");
        first.EnsureSuccessStatusCode();
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/jobs/1001");
        conditional.Headers.IfNoneMatch.Add(etag!);
        var second = await _client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Different_resources_have_different_etags()
    {
        var a = (await _client.GetAsync("/api/jobs/1001")).Headers.ETag!.Tag;
        var b = (await _client.GetAsync("/api/jobs/1002")).Headers.ETag!.Tag;
        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task Get_with_if_none_match_wildcard_returns_304()
    {
        // RFC 7232: "If-None-Match: *" matches any current representation.
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/jobs/1001");
        req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotModified, resp.StatusCode);
    }

    [Fact]
    public async Task Swagger_document_is_served()
    {
        var resp = await _client.GetAsync("/swagger/v1/swagger.json");
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("paths").TryGetProperty("/api/jobs", out _));
    }

    [Fact]
    public async Task Swagger_declares_typed_response_schemas()
    {
        var doc = await _client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");

        // The list endpoint's 200 must reference a concrete schema (not be untyped),
        // so generated clients get real models rather than `any`.
        var ok200 = doc.GetProperty("paths").GetProperty("/api/jobs").GetProperty("get")
            .GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Contains("PagedResult", ok200.GetProperty("$ref").GetString());

        // The single-get declares a 404, and the entity schema is a named component.
        Assert.True(doc.GetProperty("paths").GetProperty("/api/jobs/{abJobNum}").GetProperty("get")
            .GetProperty("responses").TryGetProperty("404", out _));
        Assert.True(doc.GetProperty("components").GetProperty("schemas").TryGetProperty("AbJob", out _));
    }

    /// <summary>Boots the app with env-var overrides pointing at a unique temp SQLite db.</summary>
    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath =
            Path.Combine(Path.GetTempPath(), $"abis_api_{Guid.NewGuid():N}.db");

        public ApiFactory()
        {
            // Environment variables outrank appsettings.json, so this reliably
            // redirects the app to an isolated, seeded fixture for the test run.
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            Environment.SetEnvironmentVariable("Database__Provider", "Sqlite");
            Environment.SetEnvironmentVariable("Database__ConnectionString", $"Data Source={_dbPath}");
            Environment.SetEnvironmentVariable("Database__Seed", "true");
            Environment.SetEnvironmentVariable("ApiKeys__Enabled", "true");
            Environment.SetEnvironmentVariable("ApiKeys__Keys__0", "test-key");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }
}
