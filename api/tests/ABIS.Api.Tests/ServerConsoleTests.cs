using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Abis.Api.Admin;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>The #7 server/service console (view + safe restarts). A fake IProcessRunner stands in for the
/// host so we assert the gating (disabled → 503, unit allowlist → 404, restart not permitted → 409, RBAC
/// feature → 403) and the EXACT argv the console would run — without ever touching systemctl/sudo.</summary>
public sealed class ServerConsoleTests
{
    private sealed class FakeRunner : IProcessRunner
    {
        public CommandOutcome Next = new(true, 0, "", "");
        public (string File, string[] Args)? Last;
        public Task<CommandOutcome> RunAsync(string file, IReadOnlyList<string> args, int timeoutSeconds, CancellationToken ct)
        {
            Last = (file, args.ToArray());
            return Task.FromResult(Next);
        }
    }

    private sealed class Factory(bool enabled, bool allowRestart, FakeRunner runner, string[]? hostCron = null) : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_console_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
            builder.UseSetting("Database:Seed", "true");
            builder.UseSetting("ApiKeys:Enabled", "true");
            builder.UseSetting("ApiKeys:Keys:0", "test-key");
            builder.UseSetting("Admin:ServerConsole:Enabled", enabled ? "true" : "false");
            builder.UseSetting("Admin:ServerConsole:AllowRestart", allowRestart ? "true" : "false");
            if (hostCron is not null)
                for (var i = 0; i < hostCron.Length; i++)
                    builder.UseSetting($"Admin:ServerConsole:HostCronCommand:{i}", hostCron[i]);
            builder.ConfigureTestServices(s => s.AddSingleton<IProcessRunner>(runner));   // swap the real process runner
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }

    private static HttpClient Client(WebApplicationFactory<Program> f, string? actAs = null)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        if (actAs is not null) c.DefaultRequestHeaders.Add("X-User-Login", actAs);
        return c;
    }

    [Fact]
    public async Task Console_disabled_returns_503()
    {
        using var f = new Factory(enabled: false, allowRestart: false, new FakeRunner());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Client(f).GetAsync("/api/admin/console/services")).StatusCode);
    }

    [Fact]
    public async Task Services_report_parsed_status()
    {
        var runner = new FakeRunner { Next = new(true, 0, "ActiveState=active\nSubState=running\nMainPID=4242\nActiveEnterTimestamp=Thu 2026-07-10 12:00:00 UTC\n", "") };
        using var f = new Factory(enabled: true, allowRestart: false, runner);
        var j = await Client(f).GetFromJsonAsync<JsonElement>("/api/admin/console/services");
        Assert.False(j.GetProperty("restartAllowed").GetBoolean());
        var svc = j.GetProperty("services").EnumerateArray().First();
        Assert.True(svc.GetProperty("available").GetBoolean());
        Assert.True(svc.GetProperty("active").GetBoolean());
        Assert.Equal("active", svc.GetProperty("state").GetString());
        Assert.Equal("4242", svc.GetProperty("mainPid").GetString());
    }

    [Fact]
    public async Task Unknown_unit_is_404()
    {
        using var f = new Factory(enabled: true, allowRestart: true, new FakeRunner());
        var c = Client(f);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/admin/console/services/haxor/logs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/admin/console/services/haxor/restart", null)).StatusCode);
    }

    [Fact]
    public async Task Restart_when_not_allowed_is_409()
    {
        using var f = new Factory(enabled: true, allowRestart: false, new FakeRunner());
        var r = await Client(f).PostAsync("/api/admin/console/services/abis/restart", null);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task Restart_allowed_invokes_sudo_systemctl_restart()
    {
        var runner = new FakeRunner { Next = new(true, 0, "", "") };
        using var f = new Factory(enabled: true, allowRestart: true, runner);
        var r = await Client(f).PostAsync("/api/admin/console/services/abis/restart", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("sudo", runner.Last!.Value.File);
        Assert.Equal(new[] { "-n", "systemctl", "restart", "abis" }, runner.Last!.Value.Args);
    }

    [Fact]
    public async Task Host_cron_unconfigured_is_503()
    {
        using var f = new Factory(enabled: true, allowRestart: false, new FakeRunner());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Client(f).GetAsync("/api/admin/console/host/cron")).StatusCode);
    }

    [Fact]
    public async Task Host_cron_configured_returns_text_via_the_configured_command()
    {
        var runner = new FakeRunner { Next = new(true, 0, "*/5 * * * * /opt/edi/GXS.ksh\n", "") };
        using var f = new Factory(enabled: true, allowRestart: false, runner, hostCron: ["ssh", "cronview@db01", "crontab -l"]);
        var j = await Client(f).GetFromJsonAsync<JsonElement>("/api/admin/console/host/cron");
        Assert.True(j.GetProperty("available").GetBoolean());
        Assert.Contains("GXS.ksh", j.GetProperty("text").GetString());
        Assert.Equal("ssh", runner.Last!.Value.File);   // the admin-configured argv, run verbatim
    }

    [Fact]
    public async Task A_user_without_the_Server_Admin_feature_is_forbidden()
    {
        using var f = new Factory(enabled: true, allowRestart: false, new FakeRunner());
        // jsmith is a seeded user with Order Entry / Inventory grants but NOT "Server Admin".
        var r = await Client(f, actAs: "jsmith").GetAsync("/api/admin/console/services");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
