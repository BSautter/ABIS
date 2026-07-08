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

        // Coil-ownership transfer certificate (seeded cert 8001, customers 4001 -> 4002).
        var cert = await _client.GetAsync("/api/documents/transfer-certificate/8001");
        cert.EnsureSuccessStatusCode();
        var certHtml = await cert.Content.ReadAsStringAsync();
        Assert.Contains("CERTIFICATE OF COIL OWNERSHIP TRANSFER", certHtml);
        Assert.Contains("Certificate #8001", certHtml);
        Assert.Contains("<rect", certHtml);   // barcode of the certificate number
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/documents/transfer-certificate/999999")).StatusCode);

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
    public async Task Job_requires_order_refs_and_positive_yield()
    {
        // A job must reference the order line it belongs to (w_stacker_job_details:491) -> 400.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/jobs", new { lineNum = 110, materialYield = 92.5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/jobs", new { orderAbcNum = 9001, lineNum = 110 })).StatusCode);
        // A supplied yield must be positive ("Invalid yield value.", w_stacker_job_details:272) -> 400.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/jobs", new { orderAbcNum = 9001, orderItemNum = 7001, materialYield = 0 })).StatusCode);
        // material_yield is a NUMBER(2,2) ratio (<= 0.99): a percentage-style value -> 400, not a
        // 500 (would overflow the column, ORA-01438 — found live on codi-ABIS).
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/jobs", new { orderAbcNum = 9001, orderItemNum = 7001, materialYield = 92.5 })).StatusCode);
        // Full order refs with a valid ratio yield -> 201 (yield is optional at create).
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/jobs", new { orderAbcNum = 9001, orderItemNum = 7001, lineNum = 110, materialYield = 0.92 })).StatusCode);
    }

    [Fact]
    public async Task Sheet_skid_requires_job_with_an_order()
    {
        // A sheet skid whose job can't resolve an order is refused (w_wh_business:831) -> 400.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/sheet-skids", new { abJobNum = 999999, sheetNetWt = 2000, skidPieces = 100 })).StatusCode);
        // Job 1001 belongs to order 9001 -> 201.
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/sheet-skids", new { abJobNum = 1001, sheetNetWt = 2000, skidPieces = 100 })).StatusCode);
    }

    [Fact]
    public async Task Part_edge_trim_tolerance_is_enforced()
    {
        // The part-master trimming spec shares the order-item edge-trim rule
        // (legacy w_part_num_new / w_order_entry). Validate(PartWrite) lifts it via the same helper.
        static object Part(double? inc, double? trm) => new
        {
            customerId = 4001, enduserPartNum = "PN-PART-TRIM", sheetType = "RECTANGLE",
            trimmingRequired = "Y", incomingCoilWidth = inc, trimmedCoilWidth = trm, trimTypeCode = 1,
        };
        // Under tolerance (0.1" < 1.5"), over tolerance (13" > 12"), and incoming < trimmed -> 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/parts", Part(48.0, 47.9))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/parts", Part(60.0, 47.0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/parts", Part(40.0, 45.0))).StatusCode);
        // Trimming required but trim data missing (widths + trimTypeCode) -> 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/parts",
            new { customerId = 4001, enduserPartNum = "PN-PART-TRIM", sheetType = "RECTANGLE", trimmingRequired = "Y" })).StatusCode);
        // Valid trim (2.0" within tolerance) -> 201; trimming not required -> widths irrelevant -> 201.
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/parts", Part(48.0, 46.0))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/parts",
            new { customerId = 4001, enduserPartNum = "PN-PART-NOTRIM", sheetType = "RECTANGLE", trimmingRequired = "N" })).StatusCode);
        // customerId is required (legacy: a part belongs to a customer) -> 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/parts",
            new { enduserPartNum = "PN-PART-NOCUST", sheetType = "RECTANGLE", trimmingRequired = "N" })).StatusCode);
        // Out-of-tolerance is OVERRIDABLE: override flag without a user -> 400; with a user -> 201.
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/parts",
            new { customerId = 4001, enduserPartNum = "PN-PART-TRIM", sheetType = "RECTANGLE", trimmingRequired = "Y", incomingCoilWidth = 60.0, trimmedCoilWidth = 47.0, trimTypeCode = 1, trimmedWidthOverridden = "Y" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/parts",
            new { customerId = 4001, enduserPartNum = "PN-PART-TRIM", sheetType = "RECTANGLE", trimmingRequired = "Y", incomingCoilWidth = 60.0, trimmedCoilWidth = 47.0, trimTypeCode = 1, trimmedWidthOverridden = "Y", trimmedWidthOverrideUser = "qa" })).StatusCode);
    }

    [Fact]
    public async Task Part_save_clears_trim_fields_and_derives_pieces_skid()
    {
        static bool IsNullOrAbsent(JsonElement o, string name) =>
            !o.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null;

        // Trimming not required: any submitted trim widths are cleared at save (w_part_num_new:562).
        var noTrim = await _client.PostAsJsonAsync("/api/parts", new
        {
            customerId = 4001, enduserPartNum = "PN-NORM-1", sheetType = "RECTANGLE",
            trimmingRequired = "N", incomingCoilWidth = 48.5, trimmedCoilWidth = 46.0, trimTypeCode = 1,
        });
        Assert.Equal(HttpStatusCode.Created, noTrim.StatusCode);
        var noTrimBody = await noTrim.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(IsNullOrAbsent(noTrimBody, "incomingCoilWidth"));
        Assert.True(IsNullOrAbsent(noTrimBody, "trimmedCoilWidth"));
        Assert.True(IsNullOrAbsent(noTrimBody, "trimTypeCode"));

        // pieces_skid omitted -> derived as Int(max_skid_wt / theoretical_unit_wt) = 4000/2 = 2000.
        var derive = await _client.PostAsJsonAsync("/api/parts", new
        {
            customerId = 4001, enduserPartNum = "PN-NORM-2", sheetType = "RECTANGLE",
            maxSkidWt = 4000, theoreticalUnitWt = 2.0, trimmingRequired = "N",
        });
        var deriveBody = await derive.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2000, deriveBody.GetProperty("piecesSkid").GetInt32());

        // An explicit pieces_skid is preserved — the derivation only fills a missing value.
        var kept = await _client.PostAsJsonAsync("/api/parts", new
        {
            customerId = 4001, enduserPartNum = "PN-NORM-3", sheetType = "RECTANGLE",
            maxSkidWt = 4000, theoreticalUnitWt = 2.0, piecesSkid = 123, trimmingRequired = "N",
        });
        var keptBody = await kept.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(123, keptBody.GetProperty("piecesSkid").GetInt32());
    }

    [Fact]
    public async Task Scrap_skid_requires_net_weight()
    {
        // Legacy refuses a null/zero scrap-skid net weight ("Skid Net Weight must be populated",
        // w_office_skid_entry:5413).
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/scrap-skids", new { scrapAbJobNum = "1001", scrapAlloy2 = "3003" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/scrap-skids", new { scrapAbJobNum = "1001", scrapNetWt = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/scrap-skids", new { scrapAbJobNum = "1001", scrapNetWt = 50, scrapTareWt = -5 })).StatusCode);
        // A real net weight -> 201.
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/scrap-skids", new { scrapAbJobNum = "1001", scrapAlloy2 = "3003", scrapNetWt = 50, scrapType = 1 })).StatusCode);
    }

    [Fact]
    public async Task Prod_folder_note_requires_existing_job()
    {
        // A note against a phantom job -> 404 (legacy "Job X does not exist.", w_e_car_folder:537).
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PostAsJsonAsync("/api/prod-folder/jobs/999999/notes", new { userId = 9001, notes = "phantom" })).StatusCode);
        // A note on a real job with a known author -> 201.
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/prod-folder/jobs/1001/notes", new { userId = 9001, notes = "real note" })).StatusCode);
    }

    [Fact]
    public async Task Receiving_coil_cash_date_is_validated()
    {
        const string url = "/api/receiving-bols/5501/coils";
        var year = DateTime.Today.Year; // keep the test in the rolling [year-2 .. year] window
        // Malformed (not 8 digits) -> 400.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync(url, new { coilOrgNum = "CD-1", cashDate = "3/15/26" })).StatusCode);
        // Month out of range (13) -> 400.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync(url, new { coilOrgNum = "CD-2", cashDate = $"1315{year}" })).StatusCode);
        // Year outside the last-two-years window -> 400.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync(url, new { coilOrgNum = "CD-3", cashDate = "03151999" })).StatusCode);
        // Well-formed, in-window (MMDDYYYY) -> 201.
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync(url, new { coilOrgNum = "CD-4", cashDate = $"0315{year}" })).StatusCode);
        // Cash date omitted -> 201 (presence is a deferred, customer-conditional rule).
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync(url, new { coilOrgNum = "CD-5" })).StatusCode);
    }

    [Fact]
    public async Task Shift_is_unique_per_line_schedule_and_day()
    {
        var day = new DateTime(2026, 5, 20, 6, 0, 0, DateTimeKind.Utc);
        object Shift(object start, int sched, long line) => new { startTime = start, scheduleType = sched, lineNum = line };
        // First shift for (line 220, schedule 1, 2026-05-20) -> 201.
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/shifts", Shift(day, 1, 220))).StatusCode);
        // Second shift, same line + schedule + day (even a different hour) -> 409 conflict.
        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PostAsJsonAsync("/api/shifts", Shift(day.AddHours(9), 1, 220))).StatusCode);
        // A different schedule type -> 201; a different day -> 201; a different line -> 201.
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/shifts", Shift(day, 2, 220))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/shifts", Shift(day.AddDays(1), 1, 220))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/shifts", Shift(day, 1, 221))).StatusCode);
    }

    [Fact]
    public async Task Shift_time_window_is_validated()
    {
        var start = new DateTime(2026, 1, 15, 6, 0, 0, DateTimeKind.Utc);
        // A shift must have a start time (legacy "Invalid Date Info" on null start/end) -> 400.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/shifts", new { lineNum = 110, operatorInitial = "QA" })).StatusCode);
        // End before start -> 400 (w_shift_info_new:130 "ending time is before starting time").
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/shifts", new { startTime = start, endTime = start.AddHours(-2), lineNum = 110 })).StatusCode);
        // Open shift (start only, no end yet) -> 201: a shift is opened at start, closed at end.
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/shifts", new { startTime = start, lineNum = 110, operatorInitial = "QA" })).StatusCode);
        // Completed shift (end after start) -> 201.
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/shifts", new { startTime = start, endTime = start.AddHours(8), lineNum = 110, operatorInitial = "QA" })).StatusCode);
    }

    [Fact]
    public async Task Dimension_check_input_is_validated()
    {
        const string url = "/api/coil-eval/skids/3001/dimension-checks";
        // Missing checkedBy (auditor) -> 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(url, new { width = 48.0 })).StatusCode);
        // Blank check with no measurements would silently default to in_spec=1 (pass) -> 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(url, new { checkedBy = "qa" })).StatusCode);
        // in_spec outside {0,1} -> 400; non-positive measurement -> 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(url, new { checkedBy = "qa", width = 48.0, inSpec = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(url, new { checkedBy = "qa", width = 0.0 })).StatusCode);
        // Valid fail record -> 201, and the entered in_spec (0) is honored (not defaulted to pass).
        var ok = await _client.PostAsJsonAsync(url, new { checkedBy = "qa", pcNumber = 1, gauge = 0.125, width = 48.0, inSpec = 0 });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var created = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, created.GetProperty("inSpec").GetInt32());
    }

    // Post the given body with the API key plus an optional X-User-Login (simulates an OIDC
    // end-user for the security gate; null = pure API-key service account).
    private async Task<HttpResponseMessage> PostAsUser(string? login, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (login is not null) req.Headers.Add("X-User-Login", login);
        return await _client.SendAsync(req);
    }

    [Fact]
    public async Task Domain_write_endpoints_enforce_feature_gate()
    {
        // Intentionally INVALID body (missing required sheetType): a caller past the gate
        // gets 400 at validation and NO row is created — keeps this test free of side effects
        // on order 9001, which other tests count.
        object badItem() => new { enduserPartNum = "PN-GATE" };
        // jsmith holds Write on "Order Entry" (direct grant) -> gate lets it through (then 400).
        Assert.NotEqual(HttpStatusCode.Forbidden, (await PostAsUser("jsmith", "/api/orders/9001/items", badItem())).StatusCode);
        // mlee has only "User Control"; no "Order Entry" grant -> gated 403 before the handler.
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsUser("mlee", "/api/orders/9001/items", badItem())).StatusCode);
        // No X-User-Login = API-key service account -> bypasses the gate (rollout policy).
        Assert.NotEqual(HttpStatusCode.Forbidden, (await PostAsUser(null, "/api/orders/9001/items", badItem())).StatusCode);

        // A different feature: coils gate on "Inventory(Coil)". mlee lacks it -> 403 at the gate,
        // before the handler, so nothing is inserted regardless of the body.
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsUser("mlee", "/api/coils", new { coilOrgNum = "ORG-GATE" })).StatusCode);

        // A read (GET) is never gated, even for a user (mlee) with no grant on that feature.
        var getReq = new HttpRequestMessage(HttpMethod.Get, "/api/orders");
        getReq.Headers.Add("X-User-Login", "mlee");
        Assert.NotEqual(HttpStatusCode.Forbidden, (await _client.SendAsync(getReq)).StatusCode);
    }

    [Fact]
    public async Task Part_applied_to_an_order_cannot_be_modified()
    {
        // Seeded item 7003 references part 6003 -> in use -> modify blocked with 409.
        var inUse = await _client.PutAsJsonAsync("/api/parts/6003", new { customerId = 4002, enduserPartNum = "PN-3003-C", sheetType = "PLATE" });
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        // The same guard covers the part's geometry (an applied part is frozen entirely).
        var inUseShape = await _client.PutAsJsonAsync("/api/parts/6003/shape", new { shapeType = "RECTANGLE" });
        Assert.Equal(HttpStatusCode.Conflict, inUseShape.StatusCode);
        // Part 6001 is not referenced by any order_item -> both the record and geometry updates pass the guard.
        var free = await _client.PutAsJsonAsync("/api/parts/6001", new { customerId = 4001, enduserPartNum = "PN-3003-A", sheetType = "RECTANGLE" });
        Assert.NotEqual(HttpStatusCode.Conflict, free.StatusCode);
        var freeShape = await _client.PutAsJsonAsync("/api/parts/6001/shape", new { shapeType = "RECTANGLE" });
        Assert.NotEqual(HttpStatusCode.Conflict, freeShape.StatusCode);
    }

    [Fact]
    public async Task Coil_transfer_to_current_owner_is_rejected()
    {
        // Seed coil 5002 is owned by customer 4001. Transferring it to 4001 is a no-op -> 409.
        var noop = await _client.PostAsJsonAsync("/api/coil-ownership/transfers",
            new { coilAbcNumOrig = 5002, customerIdNew = 4001 });
        Assert.Equal(HttpStatusCode.Conflict, noop.StatusCode);
        // A real change of owner (4001 -> 4002) is allowed and issues a certificate.
        var real = await _client.PostAsJsonAsync("/api/coil-ownership/transfers",
            new { coilAbcNumOrig = 5002, customerIdNew = 4002 });
        Assert.Equal(HttpStatusCode.Created, real.StatusCode);
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
        // The two seeded skids (3001, 3002) are present; other tests may add more to the shared fixture,
        // so assert their presence rather than an exact count.
        bool has3001 = false, has3002 = false;
        foreach (var s in body.EnumerateArray())
        {
            var num = s.GetProperty("sheetSkidNum").GetInt64();
            if (num == 3001L) has3001 = true;
            if (num == 3002L) has3002 = true;
        }
        Assert.True(has3001 && has3002, "both seeded job-1001 skids present");
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
    public async Task Finished_job_cannot_be_patched()
    {
        // Seed job 1003 is Done (job_status 0) -> any modification is rejected (409).
        var resp = await _client.PatchAsJsonAsync("/api/jobs/1003", new { jobNotes = "try to edit" });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
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
        // net_wt + width + a >=4-char org number are now required (coil integrity guard).
        var resp = await _client.PostAsJsonAsync("/api/coils",
            new { coilAlloy2 = "6061", coilGauge = 0.25, netWt = 15000, coilWidth = 48.0, coilOrgNum = "ORG-NEW-1" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
        var created = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("coilAbcNum").GetInt64();
        var fetched = await _client.GetFromJsonAsync<JsonElement>($"/api/coils/{id}");
        Assert.Equal("6061", fetched.GetProperty("coilAlloy2").GetString());
        // net_wt_balance defaults to net_wt when the client omits it (fresh coil).
        Assert.Equal(15000, fetched.GetProperty("netWtBalance").GetDecimal());
    }

    [Fact]
    public async Task Create_coil_without_alloy_returns_400()
    {
        var resp = await _client.PostAsJsonAsync("/api/coils", new { coilGauge = 0.1 });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_coil_weight_and_orgnum_integrity_is_enforced()
    {
        // Complete, valid coil (control) -> 201. Non-seeded alloy so it doesn't skew alloy-filter counts.
        object ok() => new { coilAlloy2 = "9099", netWt = 12000, coilWidth = 48.0, coilOrgNum = "ORG-INT-1" };
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/coils", ok())).StatusCode);
        // Missing net weight, missing width, zero width, and a too-short org number each -> 400 (no row created).
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/coils",
            new { coilAlloy2 = "9099", coilWidth = 48.0, coilOrgNum = "ORG-INT-2" })).StatusCode);           // no netWt
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/coils",
            new { coilAlloy2 = "9099", netWt = 12000, coilOrgNum = "ORG-INT-3" })).StatusCode);              // no width
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/coils",
            new { coilAlloy2 = "9099", netWt = 12000, coilWidth = 0.0, coilOrgNum = "ORG-INT-4" })).StatusCode); // width 0
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/coils",
            new { coilAlloy2 = "9099", netWt = 12000, coilWidth = 48.0, coilOrgNum = "AB" })).StatusCode);    // org < 4 chars
    }

    [Fact]
    public async Task Trim_override_user_is_stamped_from_the_principal()
    {
        // A throwaway order (own item count) so we don't perturb a seeded order's assertions.
        var orderResp = await _client.PostAsJsonAsync("/api/orders/with-items", new
        {
            order = new { origCustomerId = 4001, origCustomerPo = "PO-OVR" },
            items = new[] { new { enduserPartNum = "PN-SEED", sheetType = "FLAT" } },
        });
        Assert.Equal(HttpStatusCode.Created, orderResp.StatusCode);
        var orderId = (await orderResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("order").GetProperty("orderAbcNum").GetInt64();

        // An authenticated overrider (X-User-Login jsmith, who has Order Entry Write): an
        // out-of-tolerance override records THEIR login, not the spoofed body value
        // (legacy sets trimmed_width_override_user = sqlca.logid).
        var spoof = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/items")
        {
            Content = JsonContent.Create(new
            {
                enduserPartNum = "PN-OVR", sheetType = "RECTANGLE",
                trimmingRequired = "Y", incomingCoilWidth = 60.0, trimmedCoilWidth = 47.0, trimTypeCode = 1,
                trimmedWidthOverridden = "Y", trimmedWidthOverrideUser = "SPOOFED",
            }),
        };
        spoof.Headers.Add("X-User-Login", "jsmith");
        var spoofResp = await _client.SendAsync(spoof);
        Assert.Equal(HttpStatusCode.Created, spoofResp.StatusCode);
        Assert.Equal("jsmith", (await spoofResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("trimmedWidthOverrideUser").GetString());

        // The stamp runs before validation, so an authenticated overrider needn't send the
        // field at all — omitting it still yields a stamped user and a 201 (not a 400).
        var omit = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/items")
        {
            Content = JsonContent.Create(new
            {
                enduserPartNum = "PN-OVR2", sheetType = "RECTANGLE",
                trimmingRequired = "Y", incomingCoilWidth = 60.0, trimmedCoilWidth = 47.0, trimTypeCode = 1,
                trimmedWidthOverridden = "Y",
            }),
        };
        omit.Headers.Add("X-User-Login", "jsmith");
        var omitResp = await _client.SendAsync(omit);
        Assert.Equal(HttpStatusCode.Created, omitResp.StatusCode);
        Assert.Equal("jsmith", (await omitResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("trimmedWidthOverrideUser").GetString());
    }

    [Fact]
    public async Task Coil_create_without_lot_num_succeeds_and_defaults_it()
    {
        // COIL.LOT_NUM is NOT NULL on Oracle (the fixture now mirrors that). A create that omits
        // lotNum must still succeed — the repo defaults it — instead of ORA-01400 (live-only bug
        // found deploying to codi-ABIS; SQLite previously allowed the null).
        var resp = await _client.PostAsJsonAsync("/api/coils",
            new { coilAlloy2 = "9099", netWt = 12000, coilWidth = 48.0, coilOrgNum = "ORG-LOT-1", customerId = 4001, coilMidNum = "LOTCHK" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var got = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.String, got.GetProperty("lotNum").ValueKind); // defaulted, non-null
    }

    [Fact]
    public async Task Scrap_skid_create_without_tare_defaults_to_zero()
    {
        // SCRAP_SKID.SCRAP_TARE_WT is NOT NULL on Oracle; omitting tare must default to 0, not 500.
        var resp = await _client.PostAsJsonAsync("/api/scrap-skids",
            new { scrapAbJobNum = "1001", scrapAlloy2 = "3003", scrapNetWt = 50, scrapType = 1 });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var got = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, got.GetProperty("scrapTareWt").GetDecimal());
    }

    [Fact]
    public async Task Sheet_skid_create_without_tare_defaults_to_zero()
    {
        // SHEET_SKID.SHEET_TARE_WT is NOT NULL on Oracle; omitting tare must default to 0
        // (legacy: If IsNull(tare) Then tare = 0), not ORA-01400. The typed e2e omits tare too.
        var resp = await _client.PostAsJsonAsync("/api/sheet-skids",
            new { abJobNum = 1001, sheetNetWt = 2000, skidPieces = 100 });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var got = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, got.GetProperty("sheetTareWt").GetDecimal());
    }

    [Fact]
    public async Task Duplicate_coil_is_rejected()
    {
        object Coil(string org, long cust, string mid) => new
        {
            coilAlloy2 = "9099", netWt = 12000, coilWidth = 48.0,
            coilOrgNum = org, customerId = cust, coilMidNum = mid,
        };
        // First coil with a fully-identified (org, customer, MID) -> 201.
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/coils", Coil("ORG-DUP-1", 4001, "MID-A"))).StatusCode);
        // Same org + customer + MID -> 409 duplicate (w_receiving_dock:494).
        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PostAsJsonAsync("/api/coils", Coil("ORG-DUP-1", 4001, "MID-A"))).StatusCode);
        // Same org + customer but a different MID -> 201 (a distinct coil).
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/coils", Coil("ORG-DUP-1", 4001, "MID-B"))).StatusCode);
    }

    [Fact]
    public async Task Terminal_coil_cannot_be_patched()
    {
        var create = await _client.PostAsJsonAsync("/api/coils",
            new { coilAlloy2 = "9099", netWt = 12000, coilWidth = 48.0, coilOrgNum = "ORG-TERM-1", coilStatus = 1 });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("coilAbcNum").GetInt64();
        // A non-terminal coil patches fine (status 1 -> Transferred 13).
        Assert.Equal(HttpStatusCode.OK, (await _client.PatchAsJsonAsync($"/api/coils/{id}", new { coilStatus = 13 })).StatusCode);
        // Now terminal (13) -> any further modification is rejected with 409.
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PatchAsJsonAsync($"/api/coils/{id}", new { coilLocation = "X-01" })).StatusCode);
    }

    [Fact]
    public async Task Create_sheet_skid_returns_201()
    {
        var resp = await _client.PostAsJsonAsync("/api/sheet-skids", new { abJobNum = 1001, sheetNetWt = 2000, skidPieces = 100 });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Sheet_skid_weight_bounds_and_default_status()
    {
        // Valid -> 201, and a new skid defaults to WH-ready status 8 (legacy w_wh_business:1485).
        var ok = await _client.PostAsJsonAsync("/api/sheet-skids", new { abJobNum = 1001, sheetNetWt = 2000, sheetTareWt = 50, skidPieces = 100 });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(8, (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("skidSheetStatus").GetInt32());
        // Missing net, zero net, over-weight net (>30000), and over-weight tare (>8000) each -> 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/sheet-skids", new { abJobNum = 1001, skidPieces = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/sheet-skids", new { abJobNum = 1001, sheetNetWt = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/sheet-skids", new { abJobNum = 1001, sheetNetWt = 30001 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/sheet-skids", new { abJobNum = 1001, sheetNetWt = 2000, sheetTareWt = 8001 })).StatusCode);
    }

    [Fact]
    public async Task Duplicate_security_login_is_rejected()
    {
        var u = new { loginId = "dupuser", userFirstName = "Dup", userLastName = "User", userStatus = 1 };
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/security/users", u)).StatusCode);
        // Same login, different case -> 409 (login_id is unique, case-insensitive).
        var dup = new { loginId = "DupUser", userFirstName = "Other", userLastName = "Person", userStatus = 1 };
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync("/api/security/users", dup)).StatusCode);
    }

    [Fact]
    public async Task Coil_transfer_performed_by_comes_from_the_principal()
    {
        // An OIDC end-user (X-User-Login) transferring coil 5001 (owner 4001 -> 4002): the
        // certificate's performedBy is their login, not the client-supplied "SPOOFED".
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/coil-ownership/transfers")
        {
            Content = JsonContent.Create(new { coilAbcNumOrig = 5001, customerIdNew = 4002, transferPerformedBy = "SPOOFED" }),
        };
        req.Headers.Add("X-User-Login", "auditor7");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var created = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("auditor7", created.GetProperty("transferPerformedBy").GetString());
    }

    [Fact]
    public async Task Security_grant_privilege_and_new_user_defaults()
    {
        // Grant privilege must be 0 or 1: an out-of-range value -> 400; a valid one -> 204.
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync("/api/security/users/9001/applications/4", new { privilege = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutAsJsonAsync("/api/security/users/9001/applications/4", new { privilege = 1 })).StatusCode);
        // A user needs a name (first or last).
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/security/users", new { loginId = "noname" })).StatusCode);
        // Status defaults to active (1) when omitted.
        var resp = await _client.PostAsJsonAsync("/api/security/users", new { loginId = "defactive", userFirstName = "Ann" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.Equal(1, (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userStatus").GetInt32());
    }

    [Fact]
    public async Task Minting_an_empty_bol_is_rejected()
    {
        // Seed BOL 5502 has no coil lines -> mint is a 400, not a silent Minted=0.
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/receiving-bols/5502/mint", null)).StatusCode);
        // A non-existent BOL -> 404.
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/receiving-bols/999999/mint", null)).StatusCode);
    }

    [Fact]
    public async Task Dimension_check_absolute_bounds_enforced()
    {
        const string url = "/api/coil-eval/skids/3001/dimension-checks";
        // Each measurement out of its legacy range -> 400 (no row created).
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(url, new { checkedBy = "qc", pcNumber = 100, gauge = 0.125, width = 48.0 })).StatusCode); // pc > 99
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(url, new { checkedBy = "qc", gauge = 2.0, width = 48.0 })).StatusCode);                   // gauge > 1
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(url, new { checkedBy = "qc", width = 3.0 })).StatusCode);                                 // width < 5
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(url, new { checkedBy = "qc", lengthOper = 1000.0 })).StatusCode);                         // length > 999
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(url, new { checkedBy = "qc", square = 10.0 })).StatusCode);                               // square > 9
    }

    [Fact]
    public async Task Die_validation_constrains_flag_owner_and_weight()
    {
        // Bad Y/N flag, fractional gross weight, and over-length owner each -> 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/dies", new { dieName = "D", engineeredScrapYN = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/dies", new { dieName = "D", grossWeight = 1250.5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/dies", new { dieName = "D", owner = new string('x', 33) })).StatusCode);
        // Valid Y/N + whole weight -> 201.
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/dies", new { dieName = "D-OK", engineeredScrapYN = "y", grossWeight = 500, owner = "ACME" })).StatusCode);
    }

    [Fact]
    public async Task Shipped_skid_cannot_be_warehouse_patched()
    {
        // Seed skid 3003 is shipped (status 0 = GONE) -> warehouse update rejected (409).
        var blocked = await _client.PatchAsJsonAsync("/api/sheet-skids/3003/warehouse", new { skidTicketIfWhed = "T-EDIT" });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        // Skid 3001 (status 1) is still updatable.
        var ok = await _client.PatchAsJsonAsync("/api/sheet-skids/3001/warehouse", new { skidTicketIfWhed = "T-WH-OK" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
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

    [Fact]
    public async Task Invoice_save_read_and_computation_over_http()
    {
        // Save an invoice for job 1001.
        var post = await _client.PostAsJsonAsync("/api/accounting/invoices",
            new { abJobNum = 1001, invoiceNum = "INV-HTTP-1", notes = "http test" });
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var created = await post.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INV-HTTP-1", created.GetProperty("invoiceNum").GetString());

        // Duplicate (ab_job_num, invoice_num) → 409.
        var dup = await _client.PostAsJsonAsync("/api/accounting/invoices",
            new { abJobNum = 1001, invoiceNum = "INV-HTTP-1" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        // Unknown job → 404; missing invoiceNum → 400.
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PostAsJsonAsync("/api/accounting/invoices", new { abJobNum = 999999, invoiceNum = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/accounting/invoices", new { abJobNum = 1001 })).StatusCode);

        // The saved record is listed for the job.
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/accounting/invoices?abJobNum=1001");
        Assert.Contains(list.EnumerateArray(), e => e.GetProperty("invoiceNum").GetString() == "INV-HTTP-1");

        // The computed invoice for the rejected-coil job 1002 exposes the exact billed reject.
        var comp = await _client.GetFromJsonAsync<JsonElement>("/api/accounting/invoices/1002/computation");
        Assert.Equal(1500m, comp.GetProperty("rejectedWt").GetDecimal());
        Assert.Equal(60m, comp.GetProperty("netWt").GetDecimal());
        Assert.Equal(1500m, comp.GetProperty("coils")[0].GetProperty("billedWeight").GetDecimal());
    }

    [Fact]
    public async Task Invoice_document_renders_html_for_a_job()
    {
        // Printable invoice for job 1002, stamped with the seeded invoice number/date.
        var doc = await _client.GetAsync("/api/documents/invoice/1002?invoiceNum=INV-1002-A");
        Assert.Equal(HttpStatusCode.OK, doc.StatusCode);
        Assert.Equal("text/html", doc.Content.Headers.ContentType!.MediaType);
        var html = await doc.Content.ReadAsStringAsync();
        Assert.Contains("Aluminum Blanking", html);
        Assert.Contains("INV-1002-A", html);
        Assert.Contains("Rejected", html);
        Assert.Contains("Weight summary", html);

        // Unknown job → 404.
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/documents/invoice/999999")).StatusCode);
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
