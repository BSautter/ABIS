using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Abis.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>The ABIS-owned password login: PBKDF2 hashing, the transitional passwordless rollout,
/// admin-set initial passwords (force-change), self change-password, and the RequirePassword gate.
/// Every test boots the real app against its own seeded SQLite fixture.</summary>
public sealed class PasswordAuthTests
{
    private sealed class Factory(Action<IWebHostBuilder>? extra = null) : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_pw_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
            builder.UseSetting("Database:Seed", "true");
            builder.UseSetting("ApiKeys:Enabled", "true");
            builder.UseSetting("ApiKeys:Keys:0", "test-key");
            extra?.Invoke(builder);
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

    private static async Task<(HttpStatusCode status, JsonElement body)> Login(HttpClient c, string login, string? password = null)
    {
        var resp = await c.PostAsJsonAsync("/auth/login", new { login, password });
        var body = resp.Content.Headers.ContentLength is > 0
            ? await resp.Content.ReadFromJsonAsync<JsonElement>()
            : default;
        return (resp.StatusCode, body);
    }

    // Find jsmith's user id from the seeded fixture (stable, but resolved rather than hardcoded).
    private static async Task<long> JsmithIdAsync(HttpClient admin)
    {
        var users = await admin.GetFromJsonAsync<JsonElement>("/api/security/users");
        foreach (var u in users.EnumerateArray())
            if (string.Equals(u.GetProperty("loginId").GetString(), "jsmith", StringComparison.OrdinalIgnoreCase))
                return u.GetProperty("userId").GetInt64();
        throw new Xunit.Sdk.XunitException("seed user 'jsmith' not found");
    }

    [Fact]
    public void Hasher_round_trips_and_rejects_bad_input()
    {
        var hash = PasswordHashing.Hash("Sunflower77");
        Assert.StartsWith("pbkdf2-sha256$", hash);
        Assert.True(PasswordHashing.Verify("Sunflower77", hash));
        Assert.False(PasswordHashing.Verify("sunflower77", hash));   // case-sensitive
        Assert.False(PasswordHashing.Verify("", hash));
        Assert.False(PasswordHashing.Verify("Sunflower77", null));
        Assert.False(PasswordHashing.Verify("Sunflower77", "not-a-hash"));
        // Two hashes of the same password differ (random per-user salt).
        Assert.NotEqual(hash, PasswordHashing.Hash("Sunflower77"));
    }

    [Fact]
    public async Task User_without_a_credential_signs_in_passwordless()
    {
        using var f = new Factory();
        var (status, body) = await Login(f.CreateClient(), "jsmith");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("passwordSet").GetBoolean());
        Assert.False(body.GetProperty("mustChangePassword").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Admin_set_password_then_login_requires_it_and_forces_change()
    {
        using var f = new Factory();
        var admin = Admin(f);
        var id = await JsmithIdAsync(admin);

        var set = await admin.PostAsJsonAsync($"/api/security/users/{id}/password", new { password = "Sunflower77" });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var anon = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(anon, "jsmith")).status);                 // missing
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(anon, "jsmith", "wrong")).status);          // wrong

        var (status, body) = await Login(anon, "jsmith", "Sunflower77");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("passwordSet").GetBoolean());
        Assert.True(body.GetProperty("mustChangePassword").GetBoolean());                                  // force-change
    }

    [Fact]
    public async Task User_changes_own_password_and_the_new_one_takes_effect()
    {
        using var f = new Factory();
        var admin = Admin(f);
        var id = await JsmithIdAsync(admin);
        await admin.PostAsJsonAsync($"/api/security/users/{id}/password", new { password = "Sunflower77" });

        var anon = f.CreateClient();
        var token = (await Login(anon, "jsmith", "Sunflower77")).body.GetProperty("token").GetString();

        var me = f.CreateClient();
        me.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Wrong current → 400; too-short new → 400; correct → 200.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await me.PostAsJsonAsync("/auth/change-password", new { currentPassword = "nope", newPassword = "Marigold99" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await me.PostAsJsonAsync("/auth/change-password", new { currentPassword = "Sunflower77", newPassword = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await me.PostAsJsonAsync("/auth/change-password", new { currentPassword = "Sunflower77", newPassword = "Marigold99" })).StatusCode);

        // New password works (and clears must-change); old password is rejected.
        var (okStatus, okBody) = await Login(anon, "jsmith", "Marigold99");
        Assert.Equal(HttpStatusCode.OK, okStatus);
        Assert.False(okBody.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(anon, "jsmith", "Sunflower77")).status);
    }

    [Fact]
    public async Task Change_password_requires_a_user_context()
    {
        using var f = new Factory();
        // API-key service account has no user → change-password is rejected.
        var svc = Admin(f);
        var resp = await svc.PostAsJsonAsync("/auth/change-password", new { currentPassword = "x", newPassword = "Marigold99" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task RequirePassword_blocks_users_with_no_credential()
    {
        using var f = new Factory(b => b.UseSetting("Auth:Jwt:RequirePassword", "true"));
        // jsmith has no credential set → strict mode rejects the sign-in.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(f.CreateClient(), "jsmith")).status);
    }
}
