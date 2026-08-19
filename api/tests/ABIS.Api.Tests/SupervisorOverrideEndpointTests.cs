using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The supervisor override, end to end.
///
/// <para><b>What this replaces.</b> Legacy's gate is one shared plaintext PIN from an INI file,
/// defaulting to <c>1234</c>, with unlimited attempts and no record of anything. Reproducing
/// <i>whether</i> an override is gated is parity; reproducing <i>how</i> it authenticates would be
/// shipping a guessable shared credential into a system that already has AD sign-in and
/// server-enforced RBAC.</para>
///
/// <para>So the assertions that matter here are the ones legacy could not make: that a wrong PIN is
/// refused and counted, that a locked PIN stays locked, that an unknown supervisor is indistinguishable
/// from a wrong PIN, that an authorisation is single-use, and — most of all — that <b>every attempt
/// leaves a record naming who made it</b>.</para>
/// </summary>
public sealed class SupervisorOverrideEndpointTests : IClassFixture<SupervisorOverrideEndpointTests.Factory>
{
    private readonly Factory _factory;
    private readonly HttpClient _client;
    public SupervisorOverrideEndpointTests(Factory f) { _factory = f; _client = f.Client(); }

    private const string SeededPin = "8471";     // mlee's, from the fixture
    private const string Endpoint = "/api/das/supervisor-override";

    private static object Body(string login, string pin, string action = "end-coil-out-of-balance",
        long? line = 110, long? job = 1001, long? coil = 5001, string? reason = "balance 1.8%") =>
        new { action, loginId = login, pin, lineNum = line, abJobNum = job, coilAbcNum = coil, panel = "BL110-STATION", reason };

    private async Task<JsonElement> Ask(object body)
    {
        var res = await _client.PostAsJsonAsync(Endpoint, body);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static bool Granted(JsonElement e) => e.GetProperty("granted").GetBoolean();

    // ---- Granting ---------------------------------------------------------------------------

    [Fact]
    public async Task A_supervisor_with_the_right_PIN_is_granted_an_id_to_carry()
    {
        var r = await Ask(Body("mlee", SeededPin));
        Assert.True(Granted(r));
        Assert.True(r.GetProperty("overrideId").GetInt64() > 0,
            "the grant must carry an id — that id is what ties the write to the supervisor who allowed it");
    }

    [Fact]
    public async Task The_supervisor_is_named_in_the_body_not_taken_from_the_session()
    {
        // The whole shape of this interaction: the operator is signed in at the panel, and the
        // supervisor walks over to it. A gate that authorised whoever was signed in would authorise
        // the operator asking for the override.
        var res = await _client.PostAsJsonAsync(Endpoint, new { action = "shift-override", pin = SeededPin });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task An_unknown_override_action_is_refused_with_the_list_of_real_ones()
    {
        var res = await _client.PostAsJsonAsync(Endpoint, Body("mlee", SeededPin, action: "open-the-safe"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("end-coil-out-of-balance", await res.Content.ReadAsStringAsync());
    }

    // ---- Refusing ---------------------------------------------------------------------------

    [Fact]
    public async Task A_supervisor_who_holds_NO_pin_is_refused_exactly_like_a_wrong_PIN()
    {
        // jsmith has no PIN. The answer must not distinguish "wrong PIN" from "not a supervisor" —
        // otherwise the panel becomes a way for anyone standing at it to enumerate who can authorise
        // overrides, which is a list worth having if you intend to guess one.
        var noPin = await Ask(Body("jsmith", SeededPin));
        var wrongPin = await Ask(Body("mlee", "9999"));

        Assert.False(Granted(noPin));
        Assert.False(Granted(wrongPin));
        Assert.Equal(wrongPin.GetProperty("message").GetString(), noPin.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_refusal_is_a_200_with_an_answer_not_an_error()
    {
        // The panel needs to show "that PIN was not accepted" and, when locked, when to try again. A
        // 401 would be indistinguishable from the operator's own session having expired.
        var r = await Ask(Body("mlee", "0007"));
        Assert.False(Granted(r));
        Assert.Equal(JsonValueKind.Null, r.GetProperty("overrideId").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(r.GetProperty("message").GetString()));
    }

    // ---- Lockout ------------------------------------------------------------------------------

    [Fact]
    public async Task Enough_wrong_PINs_lock_the_supervisor_out_and_the_right_PIN_stops_working()
    {
        // A four-digit PIN has a 10,000-entry search space; without a lockout a panel is a perfectly
        // good place to work through it. Uses its own user so it cannot disturb the other tests.
        using var f = new Factory();
        var c = f.Client();
        await GivePin(c, "lockme", "5182");

        for (var i = 0; i < 5; i++)
        {
            var bad = await c.PostAsJsonAsync(Endpoint, Body("lockme", "0000"));
            Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        }

        var res = await c.PostAsJsonAsync(Endpoint, Body("lockme", "5182"));   // the CORRECT PIN
        var r = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(Granted(r), "a locked PIN must not be accepted even when it is typed correctly");
        Assert.NotEqual(JsonValueKind.Null, r.GetProperty("lockedUntilUtc").ValueKind);
    }

    [Fact]
    public async Task Re_issuing_a_PIN_clears_the_lockout()
    {
        // The way out. An administrator giving someone a new PIN is the intended remedy, and leaving
        // the counter set would lock a supervisor out of a PIN they had only just been handed.
        using var f = new Factory();
        var c = f.Client();
        await GivePin(c, "lockme2", "5182");
        for (var i = 0; i < 5; i++) await c.PostAsJsonAsync(Endpoint, Body("lockme2", "0000"));

        await GivePin(c, "lockme2", "6293");
        var r = await c.PostAsJsonAsync(Endpoint, Body("lockme2", "6293"))
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<JsonElement>()).Unwrap();
        Assert.True(Granted(r));
    }

    [Fact]
    public async Task Wrong_PINs_against_an_unknown_login_cannot_lock_anybody_out()
    {
        // Failures are only counted against a PIN that exists. Counting them against any name typed at
        // the panel would let anyone disable a supervisor they can guess the login of.
        using var f = new Factory();
        var c = f.Client();
        for (var i = 0; i < 8; i++) await c.PostAsJsonAsync(Endpoint, Body("nosuchperson", "0000"));

        var r = await c.PostAsJsonAsync(Endpoint, Body("mlee", SeededPin))
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<JsonElement>()).Unwrap();
        Assert.True(Granted(r));
    }

    // ---- The audit ------------------------------------------------------------------------------

    [Fact]
    public async Task Every_attempt_is_recorded_with_who_what_and_where_INCLUDING_the_refusals()
    {
        // This is the point of the whole change. The shared 1234 could never say who authorised
        // closing a coil whose weights did not balance; the refusals are kept too, because a run of
        // them at one station is the only visible sign of someone working through the PIN space.
        using var f = new Factory();
        var c = f.Client();
        await c.PostAsJsonAsync(Endpoint, Body("mlee", SeededPin, reason: "balance 1.8% over tolerance"));
        await c.PostAsJsonAsync(Endpoint, Body("mlee", "0000"));

        var log = await c.GetFromJsonAsync<JsonElement>("/api/security/supervisor-overrides?login=mlee");
        var items = log.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        var granted = items.Single(i => i.GetProperty("outcome").GetString() == "granted");
        Assert.Equal("mlee", granted.GetProperty("loginId").GetString());
        Assert.Equal("end-coil-out-of-balance", granted.GetProperty("action").GetString());
        Assert.Equal(110, granted.GetProperty("lineNum").GetInt32());
        Assert.Equal(5001, granted.GetProperty("coilAbcNum").GetInt32());
        Assert.Equal("BL110-STATION", granted.GetProperty("panel").GetString());
        Assert.Contains("1.8%", granted.GetProperty("reason").GetString()!);
        // The log reads without a lookup table.
        Assert.False(string.IsNullOrWhiteSpace(granted.GetProperty("actionDescription").GetString()));
        Assert.Equal("Maria Lee", granted.GetProperty("supervisorName").GetString());

        Assert.Contains(items, i => i.GetProperty("outcome").GetString() == "denied");
    }

    // ---- Single use ------------------------------------------------------------------------------

    [Fact]
    public async Task A_granted_override_can_only_be_spent_once()
    {
        // Otherwise one authorisation closes every out-of-balance coil for the rest of the shift.
        using var f = new Factory();
        var c = f.Client();
        var r = await c.PostAsJsonAsync(Endpoint, Body("mlee", SeededPin))
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<JsonElement>()).Unwrap();
        var id = r.GetProperty("overrideId").GetInt64();

        var first = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/end",
            new { endWeight = 0m, coilAbcNum = 5001, abJobNum = 1001, supervisorOverrideId = id });
        Assert.NotEqual(HttpStatusCode.Conflict, first.StatusCode);   // spent (the run itself may 409 for other reasons)

        var second = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/end",
            new { endWeight = 0m, coilAbcNum = 5001, abJobNum = 1001, supervisorOverrideId = id });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("already been used", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_REFUSED_override_id_cannot_be_used_at_all()
    {
        // A refusal still writes an audit row, and that row has an id. Guessing it must not authorise
        // anything.
        using var f = new Factory();
        var c = f.Client();
        await c.PostAsJsonAsync(Endpoint, Body("mlee", "0000"));
        var log = await c.GetFromJsonAsync<JsonElement>("/api/security/supervisor-overrides?login=mlee");
        var refusedId = log.GetProperty("items").EnumerateArray().First().GetProperty("overrideId").GetInt64();

        var res = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/end",
            new { endWeight = 0m, coilAbcNum = 5001, abJobNum = 1001, supervisorOverrideId = refusedId });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Ending_a_coil_WITHOUT_an_override_still_works()
    {
        // Most coils balance. The gate exists for the ones that do not, and legacy leaves the 0.5%
        // test in the console — so the server records an authorisation when given one and demands
        // nothing when not.
        using var f = new Factory();
        var res = await f.Client().PostAsJsonAsync("/api/das/lines/110/coil-run/end",
            new { endWeight = 0m, coilAbcNum = 5001, abJobNum = 1001 });
        Assert.NotEqual(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ---- The PIN is not a password ----------------------------------------------------------------

    [Fact]
    public async Task A_PIN_cannot_be_used_to_SIGN_IN()
    {
        // The reason it lives in its own table. It is typed on a shared panel with an operator
        // watching; four digits that opened an application session would be a straight downgrade from
        // the password login it sits beside.
        using var f = new Factory();
        var res = await f.Client().PostAsJsonAsync("/api/auth/login", new { loginId = "mlee", password = SeededPin });
        Assert.NotEqual(HttpStatusCode.OK, res.StatusCode);
    }

    // ---- Who may hand a PIN out -------------------------------------------------------------------

    [Fact]
    public async Task Issuing_a_PIN_is_an_ADMIN_act_gated_on_a_feature_that_really_exists()
    {
        // Holding a PIN is the eligibility to authorise overrides, so handing one out is the grant, and
        // it is gated on "User Control" — the same real SECURITY_APPLICATION feature as the other
        // security-admin writes. No new feature name was invented for this: four names that existed
        // nowhere on the live database once hid whole pages and 403'd real work.
        //
        // Asserted through a SIGNED-IN user, not the API key. The key bypasses RBAC entirely, so an
        // API-key-only test would pass against a completely ungated endpoint.
        using var f = new Factory();
        var c = f.Client();

        // jsmith is an operator: Order Entry, no User Control.
        var denied = new HttpRequestMessage(HttpMethod.Post, "/api/security/users/9002/supervisor-pin")
            { Content = JsonContent.Create(new { pin = "5182" }) };
        denied.Headers.Add("X-User-Login", "jsmith");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(denied)).StatusCode);

        // mlee holds User Control (Write) via the Admins group.
        var allowed = new HttpRequestMessage(HttpMethod.Post, "/api/security/users/9002/supervisor-pin")
            { Content = JsonContent.Create(new { pin = "5182" }) };
        allowed.Headers.Add("X-User-Login", "mlee");
        Assert.Equal(HttpStatusCode.NoContent, (await c.SendAsync(allowed)).StatusCode);
    }

    [Fact]
    public async Task The_override_LOG_is_admin_only_too()
    {
        // It names supervisors and the panels they stood at. That is a read, but not a public one.
        using var f = new Factory();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/security/supervisor-overrides");
        req.Headers.Add("X-User-Login", "jsmith");
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Client().SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task A_PIN_that_breaks_the_rules_is_refused_at_the_admin_endpoint()
    {
        using var f = new Factory();
        var res = await f.Client().PostAsJsonAsync("/api/security/users/9002/supervisor-pin", new { pin = "1234" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("shared default", await res.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Taking_a_PIN_away_stops_the_overrides_but_KEEPS_the_history()
    {
        // Someone changing role should stop being able to authorise things. What they authorised while
        // they could is a record of decisions that were made, and deleting it would be rewriting them.
        using var f = new Factory();
        var c = f.Client();
        await c.PostAsJsonAsync(Endpoint, Body("mlee", SeededPin));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/security/users/9002/supervisor-pin")).StatusCode);

        var after = await c.PostAsJsonAsync(Endpoint, Body("mlee", SeededPin))
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<JsonElement>()).Unwrap();
        Assert.False(Granted(after));

        var log = await c.GetFromJsonAsync<JsonElement>("/api/security/supervisor-overrides?login=mlee");
        Assert.Contains(log.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("outcome").GetString() == "granted");
    }

    /// <summary>Give the login a PIN through the admin endpoints, as an administrator would, creating
    /// the user on first use. Uses the API key, which bypasses RBAC — the gating itself is asserted
    /// separately, above.</summary>
    private static async Task GivePin(HttpClient c, string login, string pin)
    {
        var userId = await FindUserId(c, login);
        if (userId is null)
        {
            var created = await c.PostAsJsonAsync("/api/security/users",
                new { loginId = login, userLastName = "Locke", userFirstName = "Sam", userStatus = 1 });
            created.EnsureSuccessStatusCode();
            userId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetInt64();
        }
        (await c.PostAsJsonAsync($"/api/security/users/{userId}/supervisor-pin", new { pin })).EnsureSuccessStatusCode();
    }

    private static async Task<long?> FindUserId(HttpClient c, string login)
    {
        // The user roster is a plain array, not a paged envelope.
        var users = await c.GetFromJsonAsync<JsonElement>("/api/security/users");
        foreach (var u in users.EnumerateArray())
            if (string.Equals(u.GetProperty("loginId").GetString(), login, StringComparison.OrdinalIgnoreCase))
                return u.GetProperty("userId").GetInt64();
        return null;
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_superpin_{Guid.NewGuid():N}.db");
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
