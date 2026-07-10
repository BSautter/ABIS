using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Abis.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>AD LDAP-bind sign-in (Auth:Ldap): /auth/login verifies the password by binding to a
/// domain controller instead of the local credential store. A fake ILdapAuthenticator stands in for
/// a real DC, so these run in CI. The empty-password test is the security-critical one — an LDAP
/// simple-bind with an empty password is an "unauthenticated bind" that succeeds, so the endpoint
/// must reject it BEFORE ever binding.</summary>
public sealed class LdapAuthTests
{
    private sealed class FakeLdap(Func<string, string, bool> validate) : ILdapAuthenticator
    {
        public bool Enabled => true;
        public int Calls { get; private set; }
        public string? LastUser { get; private set; }
        public Task<bool> ValidateAsync(string username, string password, CancellationToken ct)
        {
            Calls++;
            LastUser = username;
            return Task.FromResult(validate(username, password));
        }
    }

    private sealed class Factory(ILdapAuthenticator ldap) : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_ldap_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
            builder.UseSetting("Database:Seed", "true");
            builder.ConfigureTestServices(s => s.AddSingleton(ldap));   // swap the real DC bind for the fake
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }

    private static async Task<(HttpStatusCode status, JsonElement body)> Login(HttpClient c, string login, string? password = null)
    {
        var resp = await c.PostAsJsonAsync("/auth/login", new { login, password });
        var body = resp.Content.Headers.ContentLength is > 0
            ? await resp.Content.ReadFromJsonAsync<JsonElement>()
            : default;
        return (resp.StatusCode, body);
    }

    [Fact]
    public async Task Ad_bind_success_issues_a_token()
    {
        var ldap = new FakeLdap((u, p) => u == "jsmith" && p == "Correct-Horse");
        using var f = new Factory(ldap);
        var (status, body) = await Login(f.CreateClient(), "jsmith", "Correct-Horse");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("passwordSet").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Ad_bind_failure_is_401()
    {
        var ldap = new FakeLdap((_, _) => false);
        using var f = new Factory(ldap);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(f.CreateClient(), "jsmith", "wrong")).status);
        Assert.Equal(1, ldap.Calls);
    }

    [Fact]
    public async Task Empty_password_is_rejected_without_binding()
    {
        var ldap = new FakeLdap((_, _) => true);   // would say YES if it were ever asked
        using var f = new Factory(ldap);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(f.CreateClient(), "jsmith", "")).status);
        Assert.Equal(0, ldap.Calls);               // the bind was never attempted — no unauthenticated bind
    }

    [Fact]
    public async Task Domain_qualified_username_is_normalized_to_the_bare_login()
    {
        var ldap = new FakeLdap((_, p) => p == "pw");
        using var f = new Factory(ldap);

        Assert.Equal(HttpStatusCode.OK, (await Login(f.CreateClient(), "ABC\\jsmith", "pw")).status);
        Assert.Equal("jsmith", ldap.LastUser);      // NetBIOS DOMAIN\user stripped

        Assert.Equal(HttpStatusCode.OK, (await Login(f.CreateClient(), "jsmith@abc.local", "pw")).status);
        Assert.Equal("jsmith", ldap.LastUser);      // UPN user@domain stripped
    }

    [Fact]
    public async Task Unknown_abis_user_is_401_before_binding()
    {
        var ldap = new FakeLdap((_, _) => true);   // AD would accept...
        using var f = new Factory(ldap);
        // ...but with no security_user row there's no identity/RBAC, so it's rejected before the bind.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(f.CreateClient(), "nobody", "whatever")).status);
        Assert.Equal(0, ldap.Calls);
    }
}
