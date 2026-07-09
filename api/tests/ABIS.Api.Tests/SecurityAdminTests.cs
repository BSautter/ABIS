using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Admin user management on the Security surface: create → edit → delete a user, the
/// duplicate-login guard on rename, and that a delete cascades away the user's credential.
/// Calls run as the API-key service account (bypasses the User Control gate).</summary>
public sealed class SecurityAdminTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_secadmin_{Guid.NewGuid():N}.db");
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

    [Fact]
    public async Task Create_edit_and_delete_a_user()
    {
        using var f = new Factory();
        var admin = Admin(f);

        // Create
        var create = await admin.PostAsJsonAsync("/api/security/users",
            new { loginId = "pwtest", userFirstName = "Pat", userLastName = "Tester", userStatus = 1 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetInt64();

        // Edit — rename + deactivate
        var put = await admin.PutAsJsonAsync($"/api/security/users/{id}",
            new { loginId = "pwtest", userFirstName = "Patricia", userLastName = "Tester", userStatus = 0 });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var got = await admin.GetFromJsonAsync<JsonElement>($"/api/security/users/{id}");
        Assert.Equal("Patricia", got.GetProperty("userFirstName").GetString());
        Assert.Equal(0, got.GetProperty("userStatus").GetInt32());

        // Delete → gone
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/security/users/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/security/users/{id}")).StatusCode);
    }

    [Fact]
    public async Task Rename_to_an_existing_login_is_409()
    {
        using var f = new Factory();
        var admin = Admin(f);
        var create = await admin.PostAsJsonAsync("/api/security/users",
            new { loginId = "pwtest2", userFirstName = "Sam", userLastName = "Two", userStatus = 1 });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetInt64();

        // Rename onto the seeded 'jsmith' login → conflict.
        var put = await admin.PutAsJsonAsync($"/api/security/users/{id}",
            new { loginId = "jsmith", userFirstName = "Sam", userLastName = "Two", userStatus = 1 });
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
    }

    [Fact]
    public async Task Edit_requires_a_login_and_a_name()
    {
        using var f = new Factory();
        var admin = Admin(f);
        var create = await admin.PostAsJsonAsync("/api/security/users",
            new { loginId = "pwtest3", userFirstName = "Lee", userLastName = "Three", userStatus = 1 });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetInt64();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync($"/api/security/users/{id}", new { loginId = "", userFirstName = "Lee" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync($"/api/security/users/{id}", new { loginId = "pwtest3", userFirstName = "", userLastName = "" })).StatusCode);
    }

    [Fact]
    public async Task Delete_removes_the_password_credential_too()
    {
        using var f = new Factory();
        var admin = Admin(f);
        var create = await admin.PostAsJsonAsync("/api/security/users",
            new { loginId = "pwtest4", userFirstName = "Dana", userLastName = "Four", userStatus = 1 });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetInt64();

        // Give it a password, then delete the user.
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"/api/security/users/{id}/password", new { password = "Sunflower77" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/security/users/{id}")).StatusCode);

        // The login no longer exists (401), i.e. the credential was cascaded away with the user.
        var login = await f.CreateClient().PostAsJsonAsync("/auth/login", new { login = "pwtest4", password = "Sunflower77" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Delete_of_a_missing_user_is_404()
    {
        using var f = new Factory();
        Assert.Equal(HttpStatusCode.NotFound, (await Admin(f).DeleteAsync("/api/security/users/99999999")).StatusCode);
    }
}
