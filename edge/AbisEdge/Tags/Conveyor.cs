using Microsoft.Extensions.Configuration;

namespace AbisEdge.Tags;

/// <summary>
/// Which polled OPC tags carry the stacker→wrapper conveyor's <b>physical cell</b> bits — the photo-eye
/// / position sensors that say "a stack is HERE right now".
/// <para>Mapped by <b>location code</b>, the same 0..18 legend the DAS line board uses for
/// <c>LINE_CURRENT_STATUS.SHEET_SKID_LOCATION_0..18</c>, so a cell reading drops straight onto the
/// board with no second translation table. The legend and this mapping are ported from the legacy
/// stacker window (<c>w_da_sheet_110_stacker.srw</c>), where each cell tag's rising edge advanced the
/// tracked stack to exactly one location code:</para>
/// <code>
///   1  StackLeavingSta1LiftTblConveyor / …Sta2…   (either head's lift table)
///   2  StackEnteringConveyor1                     8  StackAtCenterOfConveyor3
///   3  StackOnConveyor1                           9  StackEnteringWrapper1
///   4  StackLeavingConveyor1                     10  StackLeavingWrapper1
///   5  StackOnConveyor2                          11  StackOnWrapper1UnloadConveyor
///   6  StackLeavingConveyor2                     12  StackAtCenterOfWrapper1UnloadConveyor
///   7  StackOnConveyor3                          14..18  the wrapper-2 run (removed from the plant)
/// </code>
/// <para><b>Location 0 (stack complete) and 13 (overhead crane) deliberately have no cell of their
/// own.</b> 0 is the stacker head's own done bit, already served by <c>/stacker</c>. 13 is not a
/// sensor at all: legacy inferred the crane from the FALLING edge of cell 12 — the stack leaving the
/// wrapper-1 unload conveyor means the crane took it. Detecting an edge needs remembered state, and
/// the edge service is deliberately stateless (raw current values only), so the crane is reported as
/// having no live cell rather than guessed at. A value may still be configured for either if the
/// plant later wires a real sensor.</para>
/// <para>Config is a map, not one setting per station, so a plant wires only the cells that physically
/// exist — <c>Edge:Opc:ConveyorCells: { "2": "stacker110.StackEnteringConveyor1", … }</c>. A value may
/// list SEVERAL tags (comma- or space-separated) for one location: station 1 legitimately has two,
/// one per stacker head, and either one means the same thing.</para>
/// </summary>
/// <param name="Cells">The default cell map, answering <c>/conveyor</c> with no <c>?line=</c>.</param>
/// <param name="ByLine">Per-line maps keyed by <c>line_num</c>, answering <c>/conveyor?line=n</c>. One
/// edge instance serves every networked line (both OPC boxes see all the presses), and each line has
/// its own stacker branch — <c>stacker110</c> vs <c>stacker84</c> — so the cells cannot be a single
/// flat map the way the one-tag-per-line settings are.</param>
public sealed record ConveyorConfig(
    IReadOnlyDictionary<int, string[]> Cells,
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, string[]>> ByLine)
{
    /// <summary>The lowest and highest location codes the DAS board knows (the legacy legend).</summary>
    public const int MinLocation = 0;
    public const int MaxLocation = 18;

    public static readonly ConveyorConfig Empty =
        new(new Dictionary<int, string[]>(), new Dictionary<int, IReadOnlyDictionary<int, string[]>>());

    /// <summary>
    /// The cell map for a line. Its own if configured; otherwise it depends on whether this is a
    /// multi-line deployment:
    /// <list type="bullet">
    /// <item><b>Per-line maps exist and this line isn't one of them → EMPTY.</b> Not the default map.
    /// Cells are physical sensors on ONE belt, so serving another line's cells here would paint that
    /// line's stacks onto this line's board — a wrong answer that looks authoritative. "No cells mapped"
    /// is the truthful one. (Caught live 2026-07-25: BL84's whole <c>stacker84</c> OPC branch is stripped
    /// while the stacker is out of service, so it has no map — and the fallback would have shown BL110's
    /// belt under BL84.)</item>
    /// <item><b>No per-line maps at all → the default map.</b> A single-stacker deployment configures
    /// only <c>ConveyorCells</c>, and there is no other line for it to be confused with.</item>
    /// </list>
    /// A call with no <c>?line=</c> always gets the default map.
    /// </summary>
    public IReadOnlyDictionary<int, string[]> For(int? line)
    {
        if (line is not int n) return Cells;
        if (ByLine.TryGetValue(n, out var m)) return m;
        return ByLine.Count == 0 ? Cells : EmptyMap;
    }

    private static readonly IReadOnlyDictionary<int, string[]> EmptyMap = new Dictionary<int, string[]>();

    /// <summary>Every configured cell tag across every line (for the poller to include), de-duplicated —
    /// one physical tag mapped to two locations must still only be polled once.</summary>
    public IEnumerable<string> Tags =>
        Cells.Values.Concat(ByLine.Values.SelectMany(m => m.Values))
            .SelectMany(t => t)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Read <c>Edge:Opc:ConveyorCells</c> (+ the per-line <c>ConveyorCellsByLine</c>). Keys are
    /// location codes; a key that isn't an integer in 0..18, or that has no tag against it, is SKIPPED
    /// rather than throwing — a typo in one line's cell map must not stop the edge service from starting
    /// and serving the scale and the run state. Returns the skipped keys so the caller can log them
    /// instead of failing silently.</summary>
    public static (ConveyorConfig Config, IReadOnlyList<string> Skipped) FromSection(
        IConfigurationSection section, IConfigurationSection? byLineSection = null)
    {
        var skipped = new List<string>();
        var cells = ReadMap(section, skipped, "");

        var byLine = new Dictionary<int, IReadOnlyDictionary<int, string[]>>();
        foreach (var lineSection in byLineSection?.GetChildren() ?? [])
        {
            if (!int.TryParse(lineSection.Key, out var lineNum))
            {
                skipped.Add($"ByLine:{lineSection.Key} (not a line number)");
                continue;
            }
            var map = ReadMap(lineSection, skipped, $"ByLine:{lineSection.Key}:");
            if (map.Count > 0) byLine[lineNum] = map;
        }
        return (new ConveyorConfig(cells, byLine), skipped);
    }

    private static Dictionary<int, string[]> ReadMap(IConfigurationSection section, List<string> skipped, string prefix)
    {
        var cells = new Dictionary<int, string[]>();
        foreach (var child in section.GetChildren())
        {
            if (!int.TryParse(child.Key, out var location) || location < MinLocation || location > MaxLocation)
            {
                skipped.Add($"{prefix}{child.Key} (not a location code {MinLocation}..{MaxLocation})");
                continue;
            }
            var tags = (child.Value ?? "")
                .Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (tags.Length == 0) { skipped.Add($"{prefix}{child.Key} (no tag)"); continue; }
            cells[location] = tags;
        }
        return cells;
    }
}

/// <summary>Whether a conveyor location is occupied, folded from the one-or-more cell tags mapped to
/// it. Same trust rule as every other edge reading: a tag that is missing, bad-quality or unparseable
/// contributes <c>null</c> (unknown) and is never fabricated into a "clear".</summary>
public static class ConveyorCell
{
    /// <summary>Fold several cell readings into one occupancy answer.
    /// <list type="bullet">
    /// <item><b>true</b> — ANY cell reads truthy. Station 1's two lift-table tags are an OR by design
    /// (a stack left <i>a</i> head), and for a single-tag station this is just that tag.</item>
    /// <item><b>false</b> — at least one cell read cleanly and none is truthy.</item>
    /// <item><b>null</b> — nothing readable. Shown as unknown, NOT as clear: an unreadable sensor must
    /// never make an occupied station look empty on the floor board.</item>
    /// </list></summary>
    public static bool? Occupied(IEnumerable<(string? Value, string? Quality)> readings)
    {
        var anyRead = false;
        foreach (var (value, quality) in readings)
        {
            var b = StackDone.Parse(value, quality);   // shared truthy/falsy rules incl. .NET-style "True"/"False"
            if (b is true) return true;
            if (b is false) anyRead = true;
        }
        return anyRead ? false : null;
    }
}
