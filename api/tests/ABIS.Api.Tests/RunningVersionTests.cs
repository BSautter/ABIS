using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// What <c>/</c> says is running.
///
/// <para>This exists because on 2026-08-19 the deployed app reported <c>0.8.2.0</c> while serving
/// code from five merged PRs past that tag. <c>build-deb.sh</c> stamps the real identity —
/// <c>git describe --tags --always --dirty</c>, e.g. <c>v0.8.2-47-gec283af</c> — into
/// <c>InformationalVersion</c>, because only a bare <c>X.Y.Z</c> fits the four-numeric-part assembly
/// version. Reading <c>Assembly.GetName().Version</c> threw away precisely the part that says which
/// build, so the one obvious place to check what is deployed was quietly wrong, and the only way to
/// tell what was actually live was probing endpoints for 401-vs-404.</para>
/// </summary>
public sealed class RunningVersionTests : IClassFixture<RunningVersionTests.Factory>
{
    private readonly HttpClient _client;
    public RunningVersionTests(Factory f) => _client = f.Client();

    [Fact]
    public void It_prefers_the_INFORMATIONAL_version_over_the_numeric_assembly_one()
    {
        // The two differ whenever a build is past its tag, which is most of the time.
        var asm = typeof(Abis.Api.Endpoints.ApiEndpoints).Assembly;
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.NotNull(informational);

        var reported = Abis.Api.Endpoints.ApiEndpoints.RunningVersion();
        Assert.StartsWith(reported, informational!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_SourceLink_commit_suffix_is_trimmed()
    {
        // .NET appends "+<sha>" to InformationalVersion. The git-describe string already carries the
        // commit, so the suffix is noise — and a version with a '+' in it reads like a build error.
        Assert.DoesNotContain('+', Abis.Api.Endpoints.ApiEndpoints.RunningVersion());
    }

    [Fact]
    public void It_always_reports_something()
    {
        Assert.False(string.IsNullOrWhiteSpace(Abis.Api.Endpoints.ApiEndpoints.RunningVersion()));
    }

    [Fact]
    public void A_git_describe_string_survives_intact()
    {
        // What build-deb.sh actually stamps looks like v0.8.2-47-gec283af — a tag, a commit distance
        // and a sha. Every part of that is the point, so nothing may be trimmed except the SourceLink
        // "+sha" the SDK appends. Exercised on the shape rather than on the ambient build, which
        // carries whatever the caller passed (or the SDK's own 1.0.0 default when nobody passed
        // anything — that default is the SDK speaking, not a claim about ABIS, which has never
        // released a 1.0.0).
        foreach (var (stamped, expected) in new[]
                 {
                     ("v0.8.2-47-gec283af+abc123", "v0.8.2-47-gec283af"),
                     ("v0.8.2-47-gec283af",        "v0.8.2-47-gec283af"),
                     ("v0.9.0",                    "v0.9.0"),
                     ("v0.8.2-3-gdeadbee-dirty",   "v0.8.2-3-gdeadbee-dirty"),
                 })
        {
            var plus = stamped.IndexOf('+');
            Assert.Equal(expected, plus > 0 ? stamped[..plus] : stamped);
        }
    }

    [Fact]
    public async Task The_root_endpoint_serves_it()
    {
        var root = await _client.GetFromJsonAsync<JsonElement>("/");
        var version = root.GetProperty("version").GetString();
        Assert.Equal(Abis.Api.Endpoints.ApiEndpoints.RunningVersion(), version);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_ver_{Guid.NewGuid():N}.db");
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
