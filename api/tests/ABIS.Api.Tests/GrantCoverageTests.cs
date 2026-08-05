using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace Abis.Api.Tests;

/// <summary>
/// Whether the plant's people actually HOLD the features the app gates on.
///
/// <para><b>Why a correct gate is not enough.</b> <c>JwtUserWorkflowTests</c> proves the mechanism
/// works: hold the grant and you are let through, lack it and you are refused. That says nothing about
/// whether anyone holds it. A gate on a feature granted to one person is indistinguishable from a
/// broken gate on the day the plant tries to use the screen — the difference is invisible in code and
/// only shows up in <c>SECURITY_APPLICATION</c>.</para>
///
/// <para><b>The precedent.</b> This project shipped a gate on four feature names that did not exist at
/// all, hiding Parts, Admin and the server console and 403'ing part and PM writes. The whole suite was
/// green, because tests authenticate with the API key and the API key bypasses the gate. The names are
/// real now; how many people hold them is the half that was never checked.</para>
///
/// <para><b>What the snapshot says.</b> Of the mapped features, all but two are held by 31–45 of the
/// 46 users on the system. <c>Part Number</c> and <c>Maintenance_logs</c> are held by <b>one user
/// each</b> — see the accepted exceptions below for what that means and why it is recorded rather than
/// silently widened.</para>
/// </summary>
public sealed class GrantCoverageTests(ITestOutputHelper output)
{
    /// <summary>Below this, a feature is not meaningfully granted and gating a whole subsystem on it
    /// locks out the plant. Deliberately low — the point is to catch "nobody has this", not to have an
    /// opinion about how many people should.</summary>
    private const int MinimumHolders = 5;

    /// <summary>Features mapped in <c>FeatureByTag</c> that are BELOW the floor on live, each with what
    /// it means. Listed so the fact is visible and decided, not so it can be waved through: every entry
    /// here is a screen the plant cannot currently use, and needs a grant change before that module
    /// goes live.</summary>
    /// <summary>Features mapped in <c>FeatureByTag</c> that are BELOW the floor on live, each with what
    /// it means. Listed so the fact is visible and decided, not so it can be waved through.
    /// <para><b>Currently empty, and that is a result, not an oversight.</b> Both entries that lived here
    /// — <c>Part Number</c> and <c>Maintenance_logs</c>, one holder each — were resolved on 2026-08-05
    /// when the plant granted the IT group Write on all 39 features. They now sit at 5.</para>
    /// <para>Note what 5 does and does not mean: it is the IT group and nobody else. Enough to
    /// administer and to pilot; NOT enough for the people who would use Parts or Maintenance daily.
    /// That is tracked in REMAINING_WORK, not here, because it is a question of WHICH people hold a
    /// grant rather than whether anyone does — and this guard only answers the second one.</para></summary>
    private static readonly Dictionary<string, string> AcceptedThinGrants = new(StringComparer.OrdinalIgnoreCase);


    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "api", "tests", "ABIS.Api.Tests", "oracle-feature-grants.tsv")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static Dictionary<string, int> LiveGrants()
    {
        var path = Path.Combine(RepoRoot(), "api", "tests", "ABIS.Api.Tests", "oracle-feature-grants.tsv");
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('\t');
            if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out var n)) map[parts[0].Trim()] = n;
        }
        Assert.NotEmpty(map);
        return map;
    }

    private static IReadOnlyDictionary<string, string> FeatureByTag()
    {
        var t = typeof(Program).Assembly.GetTypes().First(x => x.Name == "ApiEndpoints");
        var f = t.GetField("FeatureByTag", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        return (IReadOnlyDictionary<string, string>)f!.GetValue(null)!;
    }

    [Fact]
    public void Every_gated_feature_exists_on_the_live_database()
    {
        // The phantom-feature bug against real data rather than the fixture: a name absent here refuses
        // every user in the plant, and no grant can fix it because there is nothing to grant.
        var live = LiveGrants();
        var missing = FeatureByTag().Values.Distinct()
            .Where(f => !live.ContainsKey(f))
            .OrderBy(x => x).ToList();

        Assert.True(missing.Count == 0,
            "These features are gated on but do not exist in SECURITY_APPLICATION on the live database, " +
            "so every signed-in user is refused and granting cannot help: " + string.Join(", ", missing));
    }

    [Fact]
    public void No_subsystem_is_gated_on_a_feature_nobody_holds()
    {
        var live = LiveGrants();
        var thin = new List<string>();

        foreach (var (tag, feature) in FeatureByTag())
        {
            if (!live.TryGetValue(feature, out var holders)) continue;   // covered by the test above
            if (holders >= MinimumHolders) continue;
            if (AcceptedThinGrants.ContainsKey(feature)) continue;
            thin.Add($"{tag} -> '{feature}': only {holders} user(s) hold it on live");
        }

        foreach (var line in thin) output.WriteLine(line);
        Assert.True(thin.Count == 0,
            "A subsystem is gated on a feature almost nobody holds. Signing in will not help those users — " +
            "they will get a 403 from a screen that looks available. Either the mapping is wrong, or the " +
            "plant needs to widen the grant before that module goes live. Add it to AcceptedThinGrants " +
            "WITH the decision once it has been made:\n" + string.Join("\n", thin));
    }

    [Fact]
    public void The_recorded_exceptions_are_still_real()
    {
        // An exception list that outlives its reason is worse than none — it hides the next occurrence.
        // If the plant widens one of these grants, this fails and the entry gets deleted.
        var live = LiveGrants();
        var stale = AcceptedThinGrants.Keys
            .Where(f => live.TryGetValue(f, out var n) && n >= MinimumHolders)
            .ToList();

        Assert.True(stale.Count == 0,
            "These are listed as thin but are now broadly granted on live — delete the entry: " +
            string.Join(", ", stale));

        // …and every accepted exception must still be a feature something is actually gated on.
        var gated = FeatureByTag().Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphaned = AcceptedThinGrants.Keys.Where(f => !gated.Contains(f)).ToList();
        Assert.True(orphaned.Count == 0,
            "These exceptions name features nothing is gated on any more — delete them: " + string.Join(", ", orphaned));
    }

    [Fact]
    public void The_snapshot_covers_every_feature_the_live_system_defines()
    {
        // Guards the snapshot itself: a truncated capture would silently make the checks above vacuous
        // by having nothing to disagree with.
        var live = LiveGrants();
        Assert.True(live.Count >= 39,
            $"the grant snapshot holds {live.Count} features; live had 39 when captured. " +
            "Re-capture it rather than letting the coverage checks run on a partial list.");
    }
}
