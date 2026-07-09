using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>The "finish admin" RBAC editing surface: clearing a direct user grant (group privilege
/// survives), the group feature-grant editor (set + clear), group create/delete with cascade, and
/// feature create/delete with the User-Control self-lockout guard. Calls run as the API-key service
/// account (bypasses the User Control gate). Seed: jsmith(9001) direct Order Entry(1) Write + group
/// Operators(10); Operators grants Order Entry(1) RO + Inventory(2) Write; "User Control" = app 3.</summary>
public sealed class SecurityGrantAdminTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_secgrant_{Guid.NewGuid():N}.db");
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

    private static HttpClient Admin(WebApplicationFactory<Program> f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return c;
    }

    // Find the (privilege, viaGroup) for a named feature in an EffectivePermission[] payload.
    private static (int priv, bool viaGroup)? Find(JsonElement arr, string feature)
    {
        foreach (var p in arr.EnumerateArray())
            if (p.GetProperty("applicationName").GetString() == feature)
                return (p.GetProperty("privilege").GetInt32(), p.GetProperty("viaGroup").GetBoolean());
        return null;
    }

    [Fact]
    public async Task Clearing_a_direct_grant_leaves_the_group_privilege()
    {
        using var f = new Factory();
        var admin = Admin(f);

        // jsmith starts with a DIRECT Write on Order Entry (effective Write, not via group).
        var before = Find(await admin.GetFromJsonAsync<JsonElement>("/api/security/users/9001/permissions"), "Order Entry");
        Assert.Equal((1, false), before);

        // Clear the direct grant.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/security/users/9001/applications/1")).StatusCode);

        // Order Entry is still granted — now ReadOnly, via the Operators group.
        var after = Find(await admin.GetFromJsonAsync<JsonElement>("/api/security/users/9001/permissions"), "Order Entry");
        Assert.Equal((0, true), after);

        // Nothing left to clear → 404.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/security/users/9001/applications/1")).StatusCode);
    }

    [Fact]
    public async Task Group_grant_editor_sets_and_clears()
    {
        using var f = new Factory();
        var admin = Admin(f);

        // Operators (group 10) seed grants: Order Entry(1) + Inventory(2).
        var g0 = await admin.GetFromJsonAsync<JsonElement>("/api/security/groups/10/applications");
        Assert.Equal(2, g0.GetArrayLength());

        // Add a Write grant on Part Number (app 4).
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync("/api/security/groups/10/applications/4", new { privilege = 1 })).StatusCode);
        var g1 = await admin.GetFromJsonAsync<JsonElement>("/api/security/groups/10/applications");
        Assert.Equal(3, g1.GetArrayLength());
        Assert.Equal((1, false), Find(g1, "Part Number"));

        // Every Operators member now inherits Part Number Write (jsmith 9001).
        Assert.Equal((1, true), Find(await admin.GetFromJsonAsync<JsonElement>("/api/security/users/9001/permissions"), "Part Number"));

        // Clear it → back to 2, and a second clear is 404.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/security/groups/10/applications/4")).StatusCode);
        Assert.Equal(2, (await admin.GetFromJsonAsync<JsonElement>("/api/security/groups/10/applications")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/security/groups/10/applications/4")).StatusCode);
    }

    [Fact]
    public async Task Group_members_lists_the_seeded_operator()
    {
        using var f = new Factory();
        var members = await Admin(f).GetFromJsonAsync<JsonElement>("/api/security/groups/10/members");
        Assert.Contains(members.EnumerateArray(), m => m.GetProperty("loginId").GetString() == "jsmith");
    }

    [Fact]
    public async Task Create_and_delete_a_group_cascades_membership()
    {
        using var f = new Factory();
        var admin = Admin(f);

        var create = await admin.PostAsJsonAsync("/api/security/groups", new { groupName = "Testers", groupNotes = "temp" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var gid = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userGroupId").GetInt64();

        // Give it a member + a grant, then delete the whole group.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/security/users/9002/groups/{gid}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/security/groups/{gid}/applications/5", new { privilege = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/security/groups/{gid}")).StatusCode);

        // The membership link is gone — mlee (9002) no longer lists the deleted group.
        var mleeGroups = await admin.GetFromJsonAsync<JsonElement>("/api/security/users/9002/groups");
        Assert.DoesNotContain(mleeGroups.EnumerateArray(), g => g.GetProperty("userGroupId").GetInt64() == gid);

        // Deleting again → 404.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/security/groups/{gid}")).StatusCode);
    }

    [Fact]
    public async Task Create_and_delete_a_feature_cascades_group_grants()
    {
        using var f = new Factory();
        var admin = Admin(f);

        var create = await admin.PostAsJsonAsync("/api/security/applications", new { applicationName = "Temp Feature", applicationNotes = "x" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var appId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applicationId").GetInt64();

        // Grant the new feature to Operators, then delete the feature.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/security/groups/10/applications/{appId}", new { privilege = 1 })).StatusCode);
        Assert.Equal(3, (await admin.GetFromJsonAsync<JsonElement>("/api/security/groups/10/applications")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/security/applications/{appId}")).StatusCode);

        // The group's grant on it is cascaded away (back to the 2 seed grants).
        Assert.Equal(2, (await admin.GetFromJsonAsync<JsonElement>("/api/security/groups/10/applications")).GetArrayLength());
    }

    [Fact]
    public async Task Deleting_the_User_Control_feature_is_blocked()
    {
        using var f = new Factory();
        // App 3 is "User Control" — deleting it would lock every OIDC admin out of this screen.
        Assert.Equal(HttpStatusCode.Conflict, (await Admin(f).DeleteAsync("/api/security/applications/3")).StatusCode);
    }

    [Fact]
    public async Task Delete_of_a_missing_group_or_feature_is_404()
    {
        using var f = new Factory();
        var admin = Admin(f);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/security/groups/88888888")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/security/applications/88888888")).StatusCode);
    }
}
