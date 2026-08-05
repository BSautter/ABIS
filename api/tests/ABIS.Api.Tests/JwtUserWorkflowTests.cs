using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Abis.Api.Tests;

/// <summary>
/// The app as a signed-in USER, not as the API key.
///
/// <para><b>Why this exists.</b> 23 test files authenticate with <c>X-Api-Key</c>, and a service
/// account <b>bypasses the RBAC gate by rollout policy</b>. So the entire suite could be green while
/// every real person is locked out, and that is not hypothetical — it already happened here. The app
/// once gated on four feature names that did not exist in <c>SECURITY_APPLICATION</c>, which hid
/// Parts, Admin and the server console and 403'd part and PM writes. Every test passed throughout,
/// because tests are the API key and the API key skips the gate.</para>
///
/// <para><b>What this sweeps.</b> Every tag in <c>FeatureByTag</c>, read from the real map by
/// reflection so it cannot drift. For each one: mint a user holding exactly that feature at Write,
/// sign them in, and drive a representative write. A granted user must not be refused, and an
/// ungranted user must be. Both halves matter — a gate that never refuses is not a gate, and one that
/// always refuses is an outage.</para>
///
/// <para><b>What it cannot tell you.</b> Whether the plant's people actually hold these features. That
/// is live data, and it is where the sharper finding is: on .230, <c>Part Number</c> and
/// <c>Maintenance_logs</c> are each held by exactly ONE user, while every other mapped feature sits at
/// 31–45. See <c>GrantCoverageTests</c>.</para>
/// </summary>
public sealed class JwtUserWorkflowTests(ITestOutputHelper output)
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _db = Path.Combine(Path.GetTempPath(), $"abis_jwtuser_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Development");
            b.UseSetting("Database:Provider", "Sqlite");
            b.UseSetting("Database:ConnectionString", $"Data Source={_db}");
            b.UseSetting("Database:Seed", "true");
            b.UseSetting("ApiKeys:Enabled", "true");
            b.UseSetting("ApiKeys:Keys:0", "test-key");
            b.UseSetting("Auth:Jwt:SigningKey", "jwt-user-workflow-tests-signing-key-0123456789");
            b.UseSetting("Auth:Jwt:Issuer", "abis-tests");
            b.UseSetting("Auth:Jwt:Audience", "abis-tests");
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_db)) File.Delete(_db); } catch { /* best effort */ }
        }
    }

    private static HttpClient ApiKey(Factory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return c;
    }

    /// <summary>The real tag → feature map, by reflection, so this cannot describe a map that no
    /// longer exists.</summary>
    private static IReadOnlyDictionary<string, string> FeatureByTag()
    {
        var t = typeof(Program).Assembly.GetTypes().First(x => x.Name == "ApiEndpoints");
        var f = t.GetField("FeatureByTag", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        return (IReadOnlyDictionary<string, string>)f!.GetValue(null)!;
    }

    /// <summary>One representative write per tag, taken from the live route table — the first
    /// POST/PUT/PATCH carrying that tag. Derived rather than listed, so a tag whose endpoints are
    /// renamed is still covered.</summary>
    private static Dictionary<string, (string Method, string Route)> RepresentativeWrites(Factory f)
    {
        var byTag = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var methods = e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [];
            var tags = e.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.ITagsMetadata>()?.Tags;
            if (tags is not { Count: > 0 }) continue;
            var m = methods.FirstOrDefault(x => x is "POST" or "PUT" or "PATCH");
            if (m is null) continue;
            byTag.TryAdd(tags[0], (m, e.RoutePattern.RawText ?? ""));
        }
        return byTag;
    }

    private static string Fill(string route) =>
        System.Text.RegularExpressions.Regex.Replace(route, @"\{[^}]*\}", "1");

    /// <summary>Feature name → application_id, resolved once.</summary>
    private static async Task<Dictionary<string, long>> AppIdsAsync(HttpClient admin)
    {
        var apps = await admin.GetFromJsonAsync<List<JsonElement>>("/api/security/applications");
        return apps!.ToDictionary(
            a => a.GetProperty("applicationName").GetString()!,
            a => a.GetProperty("applicationId").GetInt64(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Set one user's privilege on one feature. Grants resolve from the database on EVERY
    /// request (the gate calls the repository, it does not read the token), so a sweep can sign in
    /// once and move the grant around — which is also the only way to stay under the login rate
    /// limiter, and closer to how a real grant change behaves: it takes effect without re-login.</summary>
    private static async Task SetGrant(HttpClient admin, long userId, long appId, int privilege) =>
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync($"/api/security/users/{userId}/applications/{appId}", new { privilege })).StatusCode);

    /// <summary>Create a user, grant them the named features at Write, set a password, sign in, and
    /// return a client carrying their bearer token — the exact path a person takes.</summary>
    private static async Task<HttpClient> SignInAs(Factory f, string login, params string[] features)
    {
        var admin = ApiKey(f);

        var created = await admin.PostAsJsonAsync("/api/security/users",
            new { loginId = login, userFirstName = "Test", userLastName = login, userStatus = 1 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var userId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetInt64();

        var apps = await admin.GetFromJsonAsync<List<JsonElement>>("/api/security/applications");
        foreach (var feature in features)
        {
            var app = apps!.FirstOrDefault(a =>
                string.Equals(a.GetProperty("applicationName").GetString(), feature, StringComparison.OrdinalIgnoreCase));
            Assert.True(app.ValueKind != JsonValueKind.Undefined,
                $"Feature '{feature}' is mapped in FeatureByTag but does not exist in security_application. " +
                "That is the phantom-feature bug: the gate would refuse everyone, forever.");
            var appId = app.GetProperty("applicationId").GetInt64();
            var grant = await admin.PutAsJsonAsync($"/api/security/users/{userId}/applications/{appId}", new { privilege = 1 });
            Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        }

        var pw = await admin.PostAsJsonAsync($"/api/security/users/{userId}/password",
            new { password = "Passw0rd!test", mustChange = false });
        Assert.True(pw.IsSuccessStatusCode, $"could not set a password: {pw.StatusCode}");

        var login_ = await admin.PostAsJsonAsync("/auth/login", new { login, password = "Passw0rd!test" });
        Assert.Equal(HttpStatusCode.OK, login_.StatusCode);
        var token = (await login_.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();

        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        LastUserId = userId;
        return c;
    }

    /// <summary>The id of the user the most recent <see cref="SignInAs"/> created.</summary>
    private static long LastUserId;

    private static Task<HttpResponseMessage> Write(HttpClient c, string method, string route) =>
        c.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/" + Fill(route).TrimStart('/'))
        { Content = new StringContent("{}", Encoding.UTF8, "application/json") });

    [Fact]
    public async Task Every_mapped_feature_exists_so_the_gate_can_ever_be_satisfied()
    {
        // The phantom-feature bug, caught directly: a tag mapped to a name that is not in
        // security_application refuses EVERY user forever, and no amount of granting fixes it.
        using var f = new Factory();
        var admin = ApiKey(f);
        var apps = (await admin.GetFromJsonAsync<List<JsonElement>>("/api/security/applications"))!
            .Select(a => a.GetProperty("applicationName").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var phantom = FeatureByTag().Values.Distinct()
            .Where(feature => !apps.Contains(feature))
            .OrderBy(x => x).ToList();

        Assert.True(phantom.Count == 0,
            "These features are gated on but do not exist, so every signed-in user is refused: " +
            string.Join(", ", phantom));
    }

    [Fact]
    public async Task A_user_holding_a_features_write_grant_is_not_refused_by_its_gate()
    {
        // The half that matters on day one of alpha. A person with the right grant must be able to do
        // the work; the request may still fail on its BODY (400) or state (404/409), but never on
        // authorization.
        using var f = new Factory();
        var writes = RepresentativeWrites(f);
        var admin = ApiKey(f);
        var user = await SignInAs(f, "granteduser");
        var userId = LastUserId;
        var appIds = await AppIdsAsync(admin);
        var refused = new List<string>();

        foreach (var (tag, feature) in FeatureByTag())
        {
            if (!writes.TryGetValue(tag, out var w)) continue;
            Assert.True(appIds.TryGetValue(feature, out var appId), $"feature '{feature}' does not exist");

            await SetGrant(admin, userId, appId, 1);          // hold it at Write…
            var r = await Write(user, w.Method, w.Route);
            await SetGrant(admin, userId, appId, 0);          // …and put it back

            if (r.StatusCode == HttpStatusCode.Forbidden)
                refused.Add($"{tag} -> '{feature}': {w.Method} {w.Route} answered 403 to a user who HOLDS it");
        }

        foreach (var line in refused) output.WriteLine(line);
        Assert.Empty(refused);
    }

    [Fact]
    public async Task A_user_without_the_grant_is_refused()
    {
        // The other half: a gate that never refuses is decoration. Same sweep, a user holding nothing.
        using var f = new Factory();
        var writes = RepresentativeWrites(f);
        var user = await SignInAs(f, "nogrants");
        var allowed = new List<string>();

        foreach (var (tag, feature) in FeatureByTag())
        {
            if (!writes.TryGetValue(tag, out var w)) continue;
            var r = await Write(user, w.Method, w.Route);
            if (r.StatusCode != HttpStatusCode.Forbidden)
                allowed.Add($"{tag} -> '{feature}': {w.Method} {w.Route} answered {(int)r.StatusCode} to a user with NO grants");
        }

        foreach (var line in allowed) output.WriteLine(line);
        Assert.Empty(allowed);
    }

    [Fact]
    public async Task Reads_are_open_to_any_signed_in_user()
    {
        // Deliberate: the gate is on writes only (legacy f_security_door parity). If reads started
        // refusing, every screen would go blank for anyone without a matching grant.
        using var f = new Factory();
        var user = await SignInAs(f, "readonly");

        foreach (var path in new[] { "/api/jobs", "/api/coils", "/api/lookups/lines", "/api/das/line-board" })
        {
            var r = await user.GetAsync(path);
            Assert.True(r.StatusCode != HttpStatusCode.Forbidden, $"GET {path} refused a signed-in user");
        }
    }

    [Fact]
    public async Task A_read_only_grant_does_not_confer_write()
    {
        // privilege 0 = ReadOnly, 1 = Write. The gate asks for level 1, so a ReadOnly grant must not
        // pass it — otherwise the two levels mean the same thing.
        using var f = new Factory();
        var admin = ApiKey(f);

        var created = await admin.PostAsJsonAsync("/api/security/users",
            new { loginId = "readonlygrant", userFirstName = "R", userLastName = "O", userStatus = 1 });
        var userId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetInt64();
        var apps = await admin.GetFromJsonAsync<List<JsonElement>>("/api/security/applications");
        var orderEntry = apps!.First(a => a.GetProperty("applicationName").GetString() == "Order Entry")
            .GetProperty("applicationId").GetInt64();
        await admin.PutAsJsonAsync($"/api/security/users/{userId}/applications/{orderEntry}", new { privilege = 0 });
        await admin.PostAsJsonAsync($"/api/security/users/{userId}/password", new { password = "Passw0rd!test", mustChange = false });

        var login = await admin.PostAsJsonAsync("/auth/login", new { login = "readonlygrant", password = "Passw0rd!test" });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var r = await Write(c, "POST", "/api/orders");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task A_grant_inherited_from_a_group_counts()
    {
        // Most grants on live are held through groups, not directly. If only direct grants satisfied
        // the gate, the majority of the plant would be refused while their admin screen showed them
        // as permitted.
        using var f = new Factory();
        var admin = ApiKey(f);

        var created = await admin.PostAsJsonAsync("/api/security/users",
            new { loginId = "viagroup", userFirstName = "G", userLastName = "U", userStatus = 1 });
        var userId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetInt64();

        var group = await admin.PostAsJsonAsync("/api/security/groups", new { userGroupName = "ZZ Order Entry" });
        Assert.Equal(HttpStatusCode.Created, group.StatusCode);
        var groupId = (await group.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userGroupId").GetInt64();

        var apps = await admin.GetFromJsonAsync<List<JsonElement>>("/api/security/applications");
        var orderEntry = apps!.First(a => a.GetProperty("applicationName").GetString() == "Order Entry")
            .GetProperty("applicationId").GetInt64();

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync($"/api/security/groups/{groupId}/applications/{orderEntry}", new { privilege = 1 })).StatusCode);
        Assert.True((await admin.PostAsync($"/api/security/users/{userId}/groups/{groupId}", null)).IsSuccessStatusCode);

        await admin.PostAsJsonAsync($"/api/security/users/{userId}/password", new { password = "Passw0rd!test", mustChange = false });
        var login = await admin.PostAsJsonAsync("/auth/login", new { login = "viagroup", password = "Passw0rd!test" });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var r = await Write(c, "POST", "/api/orders");
        Assert.NotEqual(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
