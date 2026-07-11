namespace AbisEdge.Tags;

/// <summary>A simulated PLC so the edge service's OPC path runs and is testable
/// with no server — returns plausible values keyed off the tag name (status /
/// count / temperature), with a counter that advances each read. Select it with
/// <c>Edge:Opc:Provider=Mock</c> (the default).</summary>
public sealed class MockTagSource : ITagSource, ITagBrowser
{
    private int _counter;

    public string Name => "mock-opc";

    public Task<IReadOnlyList<TagReading>> ReadAsync(IReadOnlyList<string> tags, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var n = Interlocked.Increment(ref _counter);
        IReadOnlyList<TagReading> readings = tags
            .Select(t => new TagReading(t, Simulate(t, n), "Good") { At = now })
            .ToList();
        return Task.FromResult(readings);
    }

    /// <summary>A tiny fake DA address space so <c>/opc/browse</c> is navigable with no server —
    /// for local UI development and tests. Two "line" branches, each with leaf tags named like the
    /// real INGEAR items (strokecnt / piececount / autorunning), so a tag picker built against the
    /// mock looks like the real thing. Branches are NodeClass <c>Object</c> (descend into them),
    /// leaf tags <c>Variable</c> (a selectable item id) — mirroring the OPC UA browser.</summary>
    public Task<IReadOnlyList<BrowsedNode>> BrowseAsync(string? nodeId, CancellationToken ct)
    {
        IReadOnlyList<BrowsedNode> nodes = (nodeId ?? "") switch
        {
            "" =>
            [
                new BrowsedNode("PLC5-BL84", "PLC5-BL84", "Object"),
                new BrowsedNode("PLC5-BL110", "PLC5-BL110", "Object"),
            ],
            "PLC5-BL84" =>
            [
                new BrowsedNode("PLC5-BL84.strokecnt", "strokecnt", "Variable"),
                new BrowsedNode("PLC5-BL84.piececount", "piececount", "Variable"),
                new BrowsedNode("PLC5-BL84.autorunning", "autorunning", "Variable"),
            ],
            "PLC5-BL110" =>
            [
                new BrowsedNode("PLC5-BL110.strokecnt", "strokecnt", "Variable"),
                new BrowsedNode("PLC5-BL110.piececount", "piececount", "Variable"),
            ],
            _ => Array.Empty<BrowsedNode>(),
        };
        return Task.FromResult(nodes);
    }

    private static string Simulate(string tag, int n)
    {
        var t = tag.ToLowerInvariant();
        if (t.Contains("status")) return n % 10 == 0 ? "DOWN" : "RUNNING";
        if (t.Contains("count")) return n.ToString();
        if (t.Contains("temp")) return (70 + (n % 8) * 0.5m).ToString("0.0");
        if (t.Contains("speed")) return (100 + (n % 20)).ToString();
        return (n % 2).ToString();
    }
}
