using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The floor board's card order and the decommissioned-line flag, both configuration rather than code.
/// <para>The plant reads its floor as BL 84, BL 78, BL 110, BL 108, BL 36, BL 24 — <c>line_num</c>
/// 7, 4, 6, 5, 2, 1. That is not numeric, alphabetical or activity-based, so it lives in
/// <c>Board:LineOrder</c>; a hardcoded sequence is something a later reader "corrects" into ascending
/// order without realising it means anything.</para>
/// <para><c>Board:DecommissionedLines</c> is deliberately SEPARATE from the order. If a line vanished
/// merely by being absent from <c>LineOrder</c>, then forgetting to list a new line would hide it —
/// the two questions ("where does it go" and "does it still exist") must fail independently.</para>
/// <para><c>/lookups/lines</c> itself never filters. It answers "what does this line_num mean", which a
/// historical job row still needs — including line 0 and a since-retired line. The BOARD decides what
/// to show. Same split already applied to <c>line_num = 0</c>.</para>
/// </summary>
public class LineBoardOrderTests
{
    // The fixture's lines are 0 / 110 / 120, not the plant's 1–7, so the test configures its own
    // ordering. What is under test is the mechanism, not the plant's particular sequence.
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_board_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
            builder.UseSetting("Database:Seed", "true");
            builder.UseSetting("ApiKeys:Enabled", "true");
            builder.UseSetting("ApiKeys:Keys:0", "test-key");
            // 120 first, then 110. 0 is listed nowhere; 110 is also marked decommissioned.
            builder.UseSetting("Board:LineOrder:0", "120");
            builder.UseSetting("Board:LineOrder:1", "110");
            builder.UseSetting("Board:DecommissionedLines:0", "110");
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }

    private static HttpClient Client(Factory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return c;
    }

    private static async Task<JsonElement[]> Lines(Factory f) =>
        (await Client(f).GetFromJsonAsync<JsonElement[]>("/api/lookups/lines"))!;

    [Fact]
    public async Task The_configured_order_comes_back_as_a_rank_lowest_first()
    {
        using var f = new Factory();
        var byNum = (await Lines(f)).ToDictionary(l => l.GetProperty("lineNum").GetInt64());

        Assert.Equal(0, byNum[120].GetProperty("displayOrder").GetInt32());
        Assert.Equal(1, byNum[110].GetProperty("displayOrder").GetInt32());
    }

    [Fact]
    public async Task A_line_the_plant_has_not_placed_has_no_rank_rather_than_being_dropped()
    {
        // It must still be RETURNED — an unlisted line sorts after the placed ones on the board, so a
        // line added to the plant cannot become invisible because nobody updated the order.
        using var f = new Factory();
        var lines = await Lines(f);
        var zero = lines.Single(l => l.GetProperty("lineNum").GetInt64() == 0);
        Assert.Equal(JsonValueKind.Null, zero.GetProperty("displayOrder").ValueKind);
    }

    [Fact]
    public async Task Decommissioned_is_flagged_but_the_line_is_still_listed()
    {
        // The flag says "not on the floor". The row still has to come back, because a job that ran on
        // that line must still be able to say so — removing it here would restate history.
        using var f = new Factory();
        var byNum = (await Lines(f)).ToDictionary(l => l.GetProperty("lineNum").GetInt64());

        Assert.True(byNum[110].GetProperty("decommissioned").GetBoolean());
        Assert.False(byNum[120].GetProperty("decommissioned").GetBoolean());
        Assert.False(byNum[0].GetProperty("decommissioned").GetBoolean());
        // Every line, none filtered out — asserted as the SET rather than a count, so a line going
        // missing names itself instead of showing up as an off-by-one. 3 is BL 60, the plant's real
        // decommissioned line; this test marks 110 instead, to prove the flag follows configuration.
        Assert.Equal(new[] { 0L, 3L, 110L, 120L }, byNum.Keys.OrderBy(k => k).ToArray());
    }

    [Fact]
    public async Task Order_and_decommissioned_are_independent()
    {
        // 110 is BOTH ordered and decommissioned; 120 is ordered and live; 0 is neither. If the two
        // settings were coupled — say a line vanished by being absent from LineOrder — omitting a new
        // line from the order would silently hide it.
        using var f = new Factory();
        var byNum = (await Lines(f)).ToDictionary(l => l.GetProperty("lineNum").GetInt64());

        Assert.Equal(1, byNum[110].GetProperty("displayOrder").GetInt32());
        Assert.True(byNum[110].GetProperty("decommissioned").GetBoolean());
        Assert.Equal(JsonValueKind.Null, byNum[0].GetProperty("displayOrder").ValueKind);
        Assert.False(byNum[0].GetProperty("decommissioned").GetBoolean());
    }
}
