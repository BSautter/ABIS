using System.Text.RegularExpressions;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// Keeps every mutating endpoint's authorization deliberate. Authentication is not the question — the
/// whole <c>/api</c> group carries <c>RequireAuthorization()</c>, so nothing here is anonymous. The
/// question is <b>feature</b> authorization: the legacy <c>f_security_door</c> parity gate, which the
/// group's endpoint filter applies by looking the endpoint's first tag up in <c>FeatureByTag</c>.
/// <para>That indirection is easy to miss. A new endpoint under a tag nobody has mapped is silently
/// writable by any user who can sign in, and it looks exactly like every gated endpoint next to it —
/// there is no missing line of code to notice in review. Three tags were in that state when this guard
/// was written (Carriers, Sketches, Lookups), and the PLC fault-code dictionary the fault lamp reads
/// from was editable by anyone with a login.</para>
/// <para>So every write must be gated by tag, gated explicitly in its handler, or listed below with a
/// reason. The list is not an exemption mechanism to reach for; it records decisions already taken.</para>
/// </summary>
public sealed class WriteEndpointGateTests
{
    /// <summary>Tags whose writes are ungated ON PURPOSE, each with the reason. Anything not here and
    /// not mapped fails the guard.
    /// <para><b>Open question, deliberately not decided in code:</b> DAS, Accounting, Sales, Trucks and
    /// Dies are plant/business writes with no mapping. They are listed to make them visible, NOT because
    /// leaving them ungated is settled. Gating them is a plant policy call, not a refactor: "Shift
    /// Control" is held by only 10 users on live, so gating the DAS shift lifecycle on it would stop
    /// every operator outside that ten from starting a shift. That decision needs the plant, not a
    /// guess — this project has already shipped a gate on features that did not exist and 403'd real
    /// work.</para></summary>
    private static readonly Dictionary<string, string> UngatedByDecision = new(StringComparer.OrdinalIgnoreCase)
    {
        ["(none)"] = "POST /auth/login is the sign-in itself and cannot require a grant.",
        ["Meta"] = "POST /auth/change-password changes the CALLER's own password; it carries RequireAuthorization() and needs no feature.",
        ["Calculator"] = "POST /calculator/piece-weight computes and persists nothing; it is a POST for the request body, not a mutation.",
        ["ScanLog"] = "Append-only scan telemetry from the handheld scanners, which authenticate with the API key (service accounts bypass the gate by rollout policy). Gating it would risk the scanners for no gain.",
        ["DAS"] = "OPEN: 12 shop-floor writes (shift lifecycle, coil runs, change-job, reverse, line queue). Needs a plant decision — see the class remarks.",
        ["Accounting"] = "OPEN: invoice creation. No legacy feature is an obvious match in SECURITY_APPLICATION; needs the plant to say which.",
        ["Sales"] = "OPEN: quotes. Legacy splits quoting into Quotation(Sheet) and Quotation(Circle); one tag cannot express both.",
        ["Trucks"] = "OPEN: truck appointments are a NEW ABIS feature replacing a spreadsheet, so no legacy feature exists to map.",
        ["Dies"] = "OPEN: die master + line-die shapes. Plausibly Production Control, but unverified against how the plant actually assigns it.",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "api", "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Source() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "api", "src", "ABIS.Api", "Endpoints", "ApiEndpoints.cs"));

    /// <summary>The tags <c>FeatureByTag</c> maps, read from the declaration itself so the guard cannot
    /// drift from the real map.</summary>
    private static HashSet<string> MappedTags(string source)
    {
        var decl = source[..source.IndexOf("public static IEndpointRouteBuilder MapAbisApi", StringComparison.Ordinal)];
        var tags = Regex.Matches(decl, @"\[""(\w+)""\]\s*=\s*""").Select(m => m.Groups[1].Value);
        return new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every mutating endpoint as (method, route, tag, explicitlyGated).</summary>
    private static List<(string Method, string Route, string Tag, bool Explicit)> WriteEndpoints(string source)
    {
        var lines = source.Split('\n');
        var found = new List<(string, string, string, bool)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var m = Regex.Match(lines[i], @"\.Map(Put|Post|Delete|Patch)\(""([^""]*)""");
            if (!m.Success) continue;

            // The endpoint's own text runs to the next .MapX( — that is where its .WithTags and any
            // RequireFeatureAsync call live.
            var block = new List<string>();
            for (var j = i; j < Math.Min(i + 40, lines.Length); j++)
            {
                if (j > i && Regex.IsMatch(lines[j], @"\.Map(Get|Put|Post|Delete|Patch)\(""")) break;
                block.Add(lines[j]);
            }
            var text = string.Join('\n', block);
            var tag = Regex.Match(text, @"\.WithTags\(""([^""]+)""");
            found.Add((m.Groups[1].Value, m.Groups[2].Value,
                       tag.Success ? tag.Groups[1].Value : "(none)",
                       text.Contains("RequireFeatureAsync", StringComparison.Ordinal)));
        }
        Assert.NotEmpty(found);
        return found;
    }

    [Fact]
    public void Every_write_endpoint_is_feature_gated_or_listed_with_a_reason()
    {
        var source = Source();
        var mapped = MappedTags(source);

        var offenders = WriteEndpoints(source)
            .Where(e => !e.Explicit && !mapped.Contains(e.Tag) && !UngatedByDecision.ContainsKey(e.Tag))
            .Select(e => $"{e.Method,-6} {e.Route}   [tag: {e.Tag}]")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} write endpoint(s) are authenticated but have NO feature gate: their tag is " +
            "not in FeatureByTag, and they do not call RequireFeatureAsync. Any user who can sign in may " +
            "call them. Either map the tag to a feature that EXISTS in SECURITY_APPLICATION, gate the " +
            "handler explicitly, or add the tag to UngatedByDecision with the reason.\n\n" +
            string.Join("\n", offenders));
    }

    [Fact]
    public void The_ungated_list_has_no_stale_entries()
    {
        // A tag left listed after it has been mapped (or after its endpoints are gone) silently exempts
        // whatever is added under that tag next.
        var source = Source();
        var mapped = MappedTags(source);
        var live = WriteEndpoints(source).Where(e => !e.Explicit).Select(e => e.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stale = UngatedByDecision.Keys
            .Where(t => !live.Contains(t) || mapped.Contains(t))
            .OrderBy(t => t).ToList();

        Assert.True(stale.Count == 0,
            "These tags are in UngatedByDecision but are now mapped, or no longer have an ungated write. " +
            "Delete them, or they exempt whatever lands under that tag next:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void The_newly_mapped_tags_point_at_features_that_actually_exist()
    {
        // The phantom-feature failure: the app once gated on four feature names absent from
        // SECURITY_APPLICATION, which 403'd real work. Every mapped feature must be a real one — checked
        // against the live application list captured in the fixture seed.
        var source = Source();
        var decl = source[..source.IndexOf("public static IEndpointRouteBuilder MapAbisApi", StringComparison.Ordinal)];
        var features = Regex.Matches(decl, @"\[""\w+""\]\s*=\s*""([^""]+)""").Select(m => m.Groups[1].Value).ToHashSet();

        foreach (var f in new[] { "Carrier Information", "Production Sketch", "Production Control" })
            Assert.Contains(f, features);

        var fixtureSeed = File.ReadAllText(Path.Combine(RepoRoot(), "api", "src", "ABIS.Api", "Data", "SqliteFixture.cs"));
        foreach (var feature in features)
            Assert.True(fixtureSeed.Contains($"\"{feature}\"", StringComparison.Ordinal),
                $"FeatureByTag maps a tag to \"{feature}\", which is not seeded in security_application. " +
                "If it is not a real feature on the live database, the gate 403s every user forever.");
    }
}
