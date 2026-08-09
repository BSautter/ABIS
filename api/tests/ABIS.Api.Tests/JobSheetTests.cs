using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The live job sheet — legacy's PRODUCTION ORDER, the document an operator works the job from.
///
/// <para>Assembled from <c>downtime2/d_report_prod_order.srd</c> and the runtime overrides in
/// <c>coil_eval/u_tabpg_job_sheet.sru</c>. <b>The live copy, not the tidy one:</b> there is a second,
/// near-identical <c>wf_show_job_sheet</c> in <c>da/w_da_sheet.srw</c> whose every display call is
/// commented out — an abandoned rewrite — and porting from it is how the sketch table came to be
/// wrong.</para>
///
/// <para>The assertions worth having here are the ones about fields that are NOT columns: the six
/// figures legacy computes or stamps over a static text, and the places where two things that look
/// like one thing are deliberately kept apart.</para>
/// </summary>
public sealed class JobSheetTests : IClassFixture<JobSheetTests.Factory>
{
    private readonly HttpClient _client;
    public JobSheetTests(Factory f) => _client = f.Client();

    private async Task<JsonElement> Sheet(long job) =>
        await _client.GetFromJsonAsync<JsonElement>($"/api/prod-folder/jobs/{job}/job-sheet");

    private static JsonElement P(JsonElement e, string name) => e.GetProperty(name);
    private static decimal? Num(JsonElement e, string name) =>
        P(e, name).ValueKind == JsonValueKind.Null ? null : P(e, name).GetDecimal();
    private static string? Str(JsonElement e, string name) =>
        P(e, name).ValueKind == JsonValueKind.Null ? null : P(e, name).GetString();

    // ---- The two customers ---------------------------------------------------------------

    [Fact]
    public async Task The_ORIGINATING_customer_and_the_END_USER_are_both_printed_and_are_not_the_same_field()
    {
        // The sheet's DataWindow joins customer on customer_order.ENDUSER_ID; the code then writes the
        // ORIGINATING customer's name over the sheet's cust_t text. Two different companies sit side
        // by side on the paper — the one who bought the metal and the one it ships to — and a port
        // that joined once would print one of them under both captions.
        var s = await Sheet(1003);
        Assert.Equal("BETA", Str(s, "customer"));      // orig_customer_id 4002, upper-cased as legacy does
        Assert.Equal("ACME", Str(s, "endUser"));       // enduser_id 4001
    }

    [Fact]
    public async Task An_order_with_no_END_USER_still_prints_its_customer()
    {
        // Order 9001 has no enduser_id. The DataWindow's own customer join is an INNER one on that
        // column, so legacy's sheet would return no row at all here; this returns the sheet with the
        // end-user caption empty. An operator holding the job number gets the document either way.
        var s = await Sheet(1001);
        Assert.Equal("ACME", Str(s, "customer"));
        Assert.Null(Str(s, "endUser"));
    }

    // ---- Figures that are computed, not selected ------------------------------------------

    [Fact]
    public async Task MAT_RECD_is_the_sum_of_what_the_job_coils_actually_committed()
    {
        // Legacy totals this by walking the nested coil report row by row in PowerBuilder. Job 1001
        // has three coils, of which only one has committed anything (200); the other two are loaded
        // and not yet run. A count of coils and a sum of their weights are different numbers and the
        // sheet prints both — "No. of coils" beside "Total WT".
        var s = await Sheet(1001);
        Assert.Equal(200m, Num(s, "materialReceived"));
        Assert.Equal(3, P(s, "coilCount").GetInt32());
    }

    [Fact]
    public async Task EST_SKID_WT_and_MAX_SCRAP_WGT_are_computed_the_way_the_sheet_computes_them()
    {
        var s = await Sheet(1001);
        Assert.Equal(625m, Num(s, "estSkidWt"));      // ceiling(50 pieces * 12.5 lb)
        Assert.Equal(3000m, Num(s, "maxScrapWt"));    // 25,000 committed - 22,000 of sheet
    }

    [Fact]
    public async Task The_printed_MATERIAL_YIELD_is_reduced_by_the_edge_trim_scrap()
    {
        // The sheet prints material_yield less edge_trim_scrap_percentage, not material_yield. Job
        // 1001 is 0.92 yield with 0.04 lost to trimming.
        var s = await Sheet(1001);
        Assert.Equal(0.92m, Num(s, "materialYield"));
        Assert.Equal(0.88m, Num(s, "materialYieldAfterTrim"));
    }

    [Fact]
    public async Task A_job_with_no_edge_trim_prints_its_yield_unchanged()
    {
        var s = await Sheet(1002);
        Assert.Equal(Num(s, "materialYield"), Num(s, "materialYieldAfterTrim"));
    }

    // ---- Shape dimensions and their tolerances ---------------------------------------------

    [Fact]
    public async Task A_rectangle_prints_a_width_and_a_length_with_their_tolerances()
    {
        var s = await Sheet(1001);
        var w = P(s, "width");
        var l = P(s, "length");
        Assert.Equal("width", Str(w, "name"));
        Assert.Equal(24.0m, Num(w, "value"));
        Assert.Equal(0.02m, Num(w, "plusTol"));
        Assert.Equal(48.0m, Num(l, "value"));
        Assert.Equal(0.03m, Num(l, "minusTol"));
    }

    [Fact]
    public async Task A_circle_prints_a_DIAMETER_and_no_length_at_all()
    {
        // Legacy fills only the width slot for a circle and guards the whole length block with
        // IF NOT(IsNull(lr_l) OR lr_l = 0). Printing "0.000" with a tolerance beside it would read as
        // a dimension to cut to — and the name matters as much: this measurement is not a width.
        var s = await Sheet(1002);
        Assert.Equal("diameter", Str(P(s, "width"), "name"));
        Assert.Equal(36.5m, Num(P(s, "width"), "value"));
        Assert.Equal(JsonValueKind.Null, P(s, "length").ValueKind);
    }

    [Fact]
    public async Task A_sheet_type_with_no_geometry_leaves_both_dimensions_absent_rather_than_zero()
    {
        // Item 7003 is a PLATE — not one of the eleven dimensioned shapes. Legacy's CHOOSE CASE falls
        // through and leaves the sheet's dimension texts at their defaults.
        var s = await Sheet(1003);
        Assert.Equal(JsonValueKind.Null, P(s, "width").ValueKind);
        Assert.Equal(JsonValueKind.Null, P(s, "length").ValueKind);
    }

    // ---- The safety warning -----------------------------------------------------------------

    [Fact]
    public async Task The_trimmed_width_warning_needs_BOTH_flags()
    {
        // "TRIMMED WIDTH OUTSIDE OF EQUIPMENT TOLERANCE. CONTACT FOREMAN BEFORE RUNNING." is the one
        // hazard notice on the sheet, and legacy nests the two tests:
        //   if(trimming_required = 'Y', if(trimmed_width_overridden = 'Y', 1, 0), 0)
        // Six order items on the live database are in this state.
        var s = await Sheet(1001);
        Assert.True(P(s, "trimmingRequired").GetBoolean());
        Assert.True(P(s, "trimmedWidthOverridden").GetBoolean());
    }

    [Fact]
    public async Task An_override_on_an_item_nobody_is_trimming_raises_NO_warning()
    {
        // Item 7002 carries the override flag but is not being trimmed. A banner that cries wolf is a
        // banner the floor stops reading, which would cost more than it saves.
        var s = await Sheet(1002);
        Assert.False(P(s, "trimmingRequired").GetBoolean());
        Assert.False(P(s, "trimmedWidthOverridden").GetBoolean());
    }

    [Fact]
    public async Task A_flag_that_is_SPACES_rather_than_empty_is_still_not_a_Y()
    {
        // Item 7003's trimming_required is three spaces — how "blank" actually reads out of a
        // VARCHAR2 on this database. Comparing to "Y" without trimming works here and in the plant;
        // comparing an untrimmed value to "" (as a nearby field once did) is what fails only on
        // Oracle. Asserted so the trim cannot be dropped as redundant.
        var s = await Sheet(1003);
        Assert.False(P(s, "trimmingRequired").GetBoolean());
    }

    // ---- By-lot ------------------------------------------------------------------------------

    [Fact]
    public async Task An_ordinary_job_states_pieces_per_skid_once_and_leaves_the_per_coil_columns_empty()
    {
        var s = await Sheet(1001);
        Assert.False(P(s, "byLot").GetBoolean());
        Assert.Equal(50, P(s, "piecesPerSkid").GetInt32());
        foreach (var c in P(s, "coils").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Null, P(c, "skids").ValueKind);
            Assert.Equal(JsonValueKind.Null, P(c, "piecesPerSkid").ValueKind);
        }
    }

    [Fact]
    public async Task A_BY_LOT_job_works_the_figures_out_per_coil_instead()
    {
        // sheet_handling_type = 1 swaps the whole DataWindow: PC./SKID becomes the literal "See Below"
        // and each coil carries its own skid and pieces-per-skid figures, because each coil is a lot
        // and lots differ in weight. The arithmetic is covered in JobSheetMathTests; what matters here
        // is that the endpoint applies it at all.
        var s = await Sheet(1003);
        Assert.True(P(s, "byLot").GetBoolean());
        var coil = Assert.Single(P(s, "coils").EnumerateArray().ToList());
        Assert.NotEqual(JsonValueKind.Null, P(coil, "skids").ValueKind);
        Assert.Equal(1m, Num(coil, "skids"));           // ceiling(40 lb * 0.95 / 40)
    }

    // ---- Partial-skid usage: two sets, deliberately -------------------------------------------

    [Fact]
    public async Task The_use_partial_skid_line_lists_EVERY_partial_assigned_to_the_job()
    {
        // Legacy's of_get_partial_wh builds this string from d_ab_job_process_partial, which does not
        // filter by who made the skid. Job 1001 has three: two of its own and one carried in.
        var s = await Sheet(1001);
        var note = Str(s, "partialSkidNote")!;
        Assert.StartsWith("use partial skid ", note);
        foreach (var skid in new[] { "2990", "3001", "3002" }) Assert.Contains(skid, note);
    }

    [Fact]
    public async Task The_partial_DETAIL_lists_only_material_carried_in_from_another_job()
    {
        // The nested d_report_ab_job_partial filters on production_sheet_item.ab_job_num <> :al_job —
        // it is telling the operator what is already on the floor to run down before opening a coil.
        // The job's OWN partials are not that, and listing them would turn a short actionable list
        // into a long one nobody reads. This is why the note and the detail must not be merged.
        var s = await Sheet(1001);
        var partial = Assert.Single(P(s, "partials").EnumerateArray().ToList());
        Assert.Equal(2990, P(partial, "sheetSkidNum").GetInt32());
        Assert.Equal("990", Str(partial, "madeOnJob"));
        Assert.False(string.IsNullOrWhiteSpace(Str(partial, "lotNum")),
            "the carried-in row exists to name the coil the metal came off");
    }

    [Fact]
    public async Task A_job_with_no_partials_says_so_with_an_empty_line_not_a_stray_caption()
    {
        var s = await Sheet(1002);
        Assert.Equal("", Str(s, "partialSkidNote"));
        Assert.Empty(P(s, "partials").EnumerateArray());
    }

    // ---- The rest of the sheet -----------------------------------------------------------------

    [Fact]
    public async Task The_seven_numbered_packaging_lines_keep_their_positions()
    {
        // They are numbered 1..7 on the paper and referred to by number. Compacting out the empty ones
        // would renumber the rest.
        var s = await Sheet(1001);
        var specs = P(s, "packagingSpecs").EnumerateArray().ToList();
        Assert.Equal(7, specs.Count);
        Assert.Equal("Stretch wrap", specs[0].GetString());
        Assert.Equal("Corner boards", specs[1].GetString());
        Assert.Equal(JsonValueKind.Null, specs[2].ValueKind);
    }

    [Fact]
    public async Task The_spec_the_operator_runs_to_is_all_present()
    {
        var s = await Sheet(1001);
        Assert.Equal("Cut-to-length 1", Str(s, "lineDesc"));
        Assert.Equal("3003", Str(s, "alloy2"));
        Assert.Equal("H14", Str(s, "temper"));
        Assert.Equal(0.125m, Num(s, "gauge"));
        Assert.Equal(0.099m, Num(s, "metalDensity"));       // joined by alloy2
        Assert.Equal("Both sides", Str(s, "trimTypeDesc")); // joined by trim_type_code
        Assert.Equal("21 ksi min", Str(s, "yieldStrength")); // joined by alloy + temper
        Assert.Equal("10%", Str(s, "shipTolerancePlus"));
        Assert.Equal("REF-A/REF-B", Str(s, "jobReferenceCodes"));
        Assert.Equal(3.5m, Num(s, "pitch"));
    }

    [Fact]
    public async Task The_drawing_is_named_from_the_live_sketch_table()
    {
        var s = await Sheet(1001);
        Assert.Equal(1, P(s, "sketchId").GetInt32());
        Assert.Equal("BRKT-A rev1", Str(s, "sketchName"));
    }

    [Fact]
    public async Task An_unknown_job_is_404()
    {
        var res = await _client.GetAsync("/api/prod-folder/jobs/999999/job-sheet");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task A_job_on_no_line_and_with_no_spec_still_returns_a_sheet()
    {
        // Job 1004 has no material yield, no men, no sketch. An operator holding a job number needs
        // the sheet to open and show what IS known — refusing because the spec is thin would leave
        // them with nothing at all.
        var res = await _client.GetAsync("/api/prod-folder/jobs/1004/job-sheet");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var s = await Sheet(1004);
        Assert.Equal(1004, P(s, "abJobNum").GetInt32());
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_jobsheet_{Guid.NewGuid():N}.db");
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
