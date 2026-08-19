using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The override-PIN coverage report.
///
/// <para>The plant's rule (2026-08-19): <b>every member of the IT group must hold a supervisor
/// override PIN.</b> Five active members on <c>.230</c>. This is what makes that rule checkable —
/// and it is a REPORT, never an issuer: a PIN its holder did not choose and does not know is not a
/// credential, so an administrator sets each one with the person there.</para>
/// </summary>
public sealed class SupervisorPinCoverageTests : IClassFixture<SupervisorPinCoverageTests.Factory>
{
    private readonly HttpClient _client;
    public SupervisorPinCoverageTests(Factory f) => _client = f.Client();

    private async Task<JsonElement> Coverage(string? group = null) =>
        await _client.GetFromJsonAsync<JsonElement>(
            "/api/security/supervisor-pin-coverage" + (group is null ? "" : $"?group={Uri.EscapeDataString(group)}"));

    [Fact]
    public async Task It_defaults_to_the_IT_group_because_that_is_the_rule()
    {
        var c = await Coverage();
        Assert.True(c.GetProperty("groupExists").GetBoolean());
        Assert.Equal(3, c.GetProperty("members").GetArrayLength());
    }

    [Fact]
    public async Task It_separates_who_HOLDS_a_pin_from_who_does_not()
    {
        // mlee holds one, jsmith does not. The shortfall is the number that has to reach zero.
        var c = await Coverage();
        Assert.Equal(1, c.GetProperty("activeWithPin").GetInt32());
        Assert.Equal(1, c.GetProperty("activeWithoutPin").GetInt32());
    }

    [Fact]
    public async Task An_INACTIVE_member_is_listed_but_is_not_counted_as_a_gap()
    {
        // kpatel is a leaver still carried in the group. Counting them leaves a shortfall that never
        // reaches zero, and a number that never reaches zero stops being read.
        var c = await Coverage();
        var kpatel = c.GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("loginId").GetString() == "kpatel");
        Assert.Equal(0, kpatel.GetProperty("userStatus").GetInt32());
        Assert.False(kpatel.GetProperty("hasPin").GetBoolean());
        Assert.Equal(1, c.GetProperty("activeWithoutPin").GetInt32());   // kpatel excluded
    }

    [Fact]
    public async Task The_group_is_matched_by_NAME_and_tolerates_case_and_padding()
    {
        // The fixture seeds the group as " it " on purpose. On the live database IT is group 10
        // today, but a Data Pump refresh imports whatever prod has — tools/grant_it_group.sql carries
        // the same scar in a comment. A report keyed to a stale id is confidently wrong about who is
        // covered, which is worse than no report.
        foreach (var name in new[] { "IT", "it", " It " })
            Assert.True((await Coverage(name)).GetProperty("groupExists").GetBoolean(), $"'{name}' should resolve");
    }

    [Fact]
    public async Task A_group_that_does_not_exist_says_so_rather_than_reporting_full_coverage()
    {
        // An empty member list with groupExists=true would read as "everyone has a PIN" — the exact
        // wrong answer for a typo'd group name.
        var c = await Coverage("Nonesuch");
        Assert.False(c.GetProperty("groupExists").GetBoolean());
        Assert.Empty(c.GetProperty("members").EnumerateArray());
    }

    [Fact]
    public async Task A_lockout_is_visible_here_too()
    {
        // A locked-out supervisor holds a PIN but cannot authorise anything. Someone reading this to
        // find out who can cover a shift needs to see that.
        using var f = new Factory();
        var c = f.Client();
        for (var i = 0; i < 5; i++)
            await c.PostAsJsonAsync("/api/das/supervisor-override",
                new { action = "shift-override", loginId = "mlee", pin = "0000" });

        var row = (await c.GetFromJsonAsync<JsonElement>("/api/security/supervisor-pin-coverage"))
            .GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("loginId").GetString() == "mlee");
        Assert.True(row.GetProperty("hasPin").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("lockedUntilUtc").ValueKind);
    }

    [Fact]
    public async Task The_report_is_admin_only()
    {
        // It names who can authorise overrides — a shortlist worth having if you mean to guess a PIN.
        using var f = new Factory();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/security/supervisor-pin-coverage");
        req.Headers.Add("X-User-Login", "jsmith");          // operator: no User Control
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Client().SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task It_never_returns_a_PIN_or_its_hash()
    {
        var raw = await _client.GetStringAsync("/api/security/supervisor-pin-coverage");
        Assert.DoesNotContain("pinHash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pbkdf2", raw, StringComparison.OrdinalIgnoreCase);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_pincov_{Guid.NewGuid():N}.db");
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
