using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>/api accepts EITHER a valid API key OR a valid JWT bearer when JWT is
/// configured (here via a symmetric signing key). Proves the dual-scheme policy.</summary>
public sealed class JwtAuthTests : IClassFixture<JwtAuthTests.JwtFactory>
{
    private readonly JwtFactory _factory;
    public JwtAuthTests(JwtFactory factory) => _factory = factory;

    private static string MakeToken(string key, string issuer, string audience, DateTime? expires = null)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer, audience: audience,
            claims: [new Claim(ClaimTypes.Name, "alice")],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: expires ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task Valid_jwt_bearer_is_accepted()
    {
        var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/jobs?pageSize=1");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            MakeToken(JwtFactory.SigningKey, JwtFactory.Issuer, JwtFactory.Audience));
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Api_key_still_works_alongside_jwt()
    {
        var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/jobs?pageSize=1");
        req.Headers.Add("X-Api-Key", "test-key");
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Bogus_or_wrongly_signed_token_is_401()
    {
        var client = _factory.CreateClient();
        // Right shape, wrong signing key → must not authenticate.
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/jobs?pageSize=1");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            MakeToken("a-totally-different-signing-key-32bytes!!", JwtFactory.Issuer, JwtFactory.Audience));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task No_credentials_is_401()
    {
        var resp = await _factory.CreateClient().GetAsync("/api/jobs?pageSize=1");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task User_login_issues_a_token_that_resolves_grants()
    {
        var client = _factory.CreateClient();
        // sign in as a seeded ABIS user (validated against security_user; passwordless on the LAN)
        var login = await client.PostAsJsonAsync("/auth/login", new { login = "jsmith" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        // the issued token authenticates AND resolves jsmith's real grants (RBAC source)
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/security/me/permissions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var perms = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, perms.StatusCode);
        var apps = (await perms.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Select(p => p.GetProperty("applicationName").GetString()).ToList();
        Assert.Contains("Order Entry", apps);   // jsmith's direct grant

        // unknown user -> 401 (no token issued)
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/auth/login", new { login = "nobody-here" })).StatusCode);
    }

    public sealed class JwtFactory : WebApplicationFactory<Program>
    {
        public const string SigningKey = "abis-test-signing-key-at-least-32-bytes!!";
        public const string Issuer = "https://test-issuer.abis.local";
        public const string Audience = "abis-api";
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_jwt_{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
            builder.UseSetting("Database:Seed", "true");
            builder.UseSetting("ApiKeys:Enabled", "true");
            builder.UseSetting("ApiKeys:Keys:0", "test-key");
            builder.UseSetting("Auth:Jwt:SigningKey", SigningKey);
            builder.UseSetting("Auth:Jwt:Issuer", Issuer);
            builder.UseSetting("Auth:Jwt:Audience", Audience);
            builder.UseSetting("Auth:Jwt:RequireHttpsMetadata", "false");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }
}
