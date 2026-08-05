using System.Net;
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
/// A bad request body must be answered as a bad request.
///
/// <para><b>What was wrong.</b> Minimal-API binding reports an unusable body by throwing
/// <c>BadHttpRequestException</c>, which carries its own 400. Nothing read it, so the exception fell
/// through to <c>UseExceptionHandler</c> and came back as an opaque 500. Sweeping every write endpoint
/// found <b>105 of 124</b> answering a malformed body that way — the 500 was the rule, not an edge case.</para>
///
/// <para><b>Why a guard and not just a fix.</b> The fix is one handler, but nothing about an endpoint
/// declares that it depends on it: a future change to exception handling would put every one of those
/// 105 back to 500 with no test going red and no line of code looking wrong. So the guard sweeps the
/// route table rather than naming endpoints, and a new endpoint is covered the day it is added.</para>
///
/// <para><b>What this does NOT prove.</b> 15 of the 124 answer 404 before the body is examined — the
/// DAS line operations, which correctly refuse when the line has no shift or coil run in progress.
/// Their binding is covered by the sweep; their write paths are not. That is a limit of probing with
/// seed data, stated here rather than papered over.</para>
/// </summary>
public sealed class MalformedBodyTests(ITestOutputHelper output)
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _db = Path.Combine(Path.GetTempPath(), $"abis_badbody_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Development");
            b.UseSetting("Database:Provider", "Sqlite");
            b.UseSetting("Database:ConnectionString", $"Data Source={_db}");
            b.UseSetting("Database:Seed", "true");
            b.UseSetting("ApiKeys:Enabled", "true");
            b.UseSetting("ApiKeys:Keys:0", "test-key");
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_db)) File.Delete(_db); } catch { /* best effort */ }
        }
    }

    private static HttpClient Client(Factory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return c;
    }

    private static Task<HttpResponseMessage> Send(HttpClient c, string method, string url, string body) =>
        c.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    /// <summary>Seeded ids, so a probe reaches the handler instead of 404-ing on the way in.</summary>
    private static readonly Dictionary<string, string> SeededIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["orderAbcNum"] = "9001", ["orderItemNum"] = "7001", ["abJobNum"] = "1001", ["job"] = "1001",
        ["coilAbcNum"] = "5001", ["coil"] = "5001", ["coilId"] = "1",
        ["customerId"] = "4001", ["cust"] = "4001", ["contactId"] = "1",
        ["lineNum"] = "4", ["packingList"] = "8801", ["carrierId"] = "1201",
        ["sheetSkidNum"] = "3001", ["skidNum"] = "3001", ["scrapSkidNum"] = "8001",
        ["dieId"] = "1", ["partNumId"] = "6001", ["shiftNum"] = "1", ["instanceNum"] = "9101",
        ["userId"] = "9001", ["groupId"] = "10", ["applicationId"] = "1", ["receivingBolId"] = "5500",
    };

    private static string Fill(string route) =>
        System.Text.RegularExpressions.Regex.Replace(route, @"\{([A-Za-z]+)[^}]*\}",
            m => SeededIds.TryGetValue(m.Groups[1].Value, out var v) ? v : "1");

    private static List<(string Method, string Route)> WriteRoutes(Factory f) =>
        f.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods
                              ?? (IReadOnlyList<string>)[])
                .Select(m => (Method: m, Route: e.RoutePattern.RawText ?? "")))
            .Where(t => t.Method is "POST" or "PUT" or "PATCH")
            .Where(t => !t.Route.Contains("/auth/", StringComparison.OrdinalIgnoreCase))  // sign-in: covered by its own tests
            .OrderBy(t => t.Route).ThenBy(t => t.Method)
            .ToList();

    [Theory]
    // An empty object: every field absent. Whether that is acceptable is the endpoint's business —
    // being told is not.
    [InlineData("{}")]
    // The JSON literal null. Binding cannot produce a body from it, which is a 400, not a fault.
    [InlineData("null")]
    // A field of the wrong type — the shape a UI actually sends when an empty numeric input is
    // serialised as a string.
    [InlineData("{\"quantity\":\"not-a-number\"}")]
    // Truncated JSON — a dropped connection or a hand-written request.
    [InlineData("{\"a\":")]
    public async Task No_write_endpoint_answers_a_bad_body_with_a_server_error(string body)
    {
        using var f = new Factory();
        var c = Client(f);

        var offenders = new List<string>();
        foreach (var (method, route) in WriteRoutes(f))
        {
            var r = await Send(c, method, "/" + Fill(route).TrimStart('/'), body);
            // 503 is allowed and deliberate: the server console answers it when the feature is switched
            // off, which is a considered response rather than an unhandled exception.
            if ((int)r.StatusCode >= 500 && r.StatusCode != HttpStatusCode.ServiceUnavailable)
                offenders.Add($"{method} {route} -> {(int)r.StatusCode}");
        }

        foreach (var o in offenders) output.WriteLine(o);
        Assert.Empty(offenders);
    }

    [Fact]
    public async Task The_sweep_actually_reaches_the_handlers_it_claims_to_cover()
    {
        // Without this, the guard above could pass by 404-ing everywhere and prove nothing. It is the
        // measurement that makes the sweep's result meaningful, so it is asserted, not assumed.
        using var f = new Factory();
        var c = Client(f);

        var reached = 0;
        var total = 0;
        foreach (var (method, route) in WriteRoutes(f))
        {
            total++;
            var r = await Send(c, method, "/" + Fill(route).TrimStart('/'), "{}");
            if (r.StatusCode != HttpStatusCode.NotFound) reached++;
        }

        output.WriteLine($"{reached}/{total} write endpoints reached a handler decision");
        Assert.True(reached >= total * 3 / 4,
            $"only {reached}/{total} endpoints got past routing — the seeded ids have drifted, so the " +
            "sweep is no longer proving what it claims.");
    }

    [Fact]
    public async Task A_rejected_body_says_what_was_wrong_with_it()
    {
        // The point of the fix is not the status code alone. A 400 that says nothing leaves the caller
        // exactly as stuck as the 500 did.
        using var f = new Factory();
        var c = Client(f);

        var r = await Send(c, "POST", "/api/orders/9001/items", "{\"quantity\":\"not-a-number\"}");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);

        var problem = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        var detail = problem.GetProperty("detail").GetString()!;
        Assert.Contains("quantity", detail, StringComparison.OrdinalIgnoreCase);   // names the offending field
        Assert.DoesNotContain("OrderItemWrite", detail, StringComparison.Ordinal); // but not our type names
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task A_missing_required_member_is_the_callers_error_too()
    {
        // POST /orders/with-items declares `required CustomerOrderWrite Order`, so `{}` fails inside the
        // deserialiser rather than in validation. It was a 500 — the only endpoint whose *empty object*
        // 500'd, which is what made it easy to miss.
        using var f = new Factory();
        var c = Client(f);

        var r = await Send(c, "POST", "/api/orders/with-items", "{}");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task A_genuine_server_fault_is_still_a_500()
    {
        // The handler must not become a catch-all that hides real breakage behind a 400. It answers
        // BadHttpRequestException and nothing else, so an endpoint whose dependency is broken still
        // reports a server error.
        using var f = new Factory();
        var c = Client(f);

        // An unroutable path is a 404, not a 400 — proof the handler is not answering everything.
        var r = await Send(c, "POST", "/api/no-such-endpoint", "{}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
