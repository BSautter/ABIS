using System.Globalization;
using System.Runtime.Versioning;

namespace AbisEdge.Tags;

/// <summary>
/// Reads Classic <b>OPC DA</b> items straight from a local DA server — the plant's INGEAR
/// <c>CimQuestInc.IGOPCAB.1</c> — via the standard <b>OPC DA Automation</b> wrapper
/// (<c>OPC.Automation</c>, from the OPC Core Components Redistributable), late-bound through COM.
///
/// <para>This is the chosen "PLC auto-downtime" path: run the edge <b>on the OPC box</b> and read
/// INGEAR locally (local COM, no DCOM, no UA bridge). Select it with
/// <c>Edge:Opc:Provider=ClassicDa</c> + <c>Edge:Opc:ProgId</c>. It is <b>Windows-only at runtime</b>
/// (COM), but late binding via <c>dynamic</c> keeps the edge building cross-platform — the Linux CI
/// build never touches COM. Requires the OPC Core Components Redistributable on the box.</para>
///
/// <para>Read model: connect once, add a subscribed group at <c>UpdateRate</c>, add each configured
/// item, then each poll returns the group's latest cached <c>Value</c>/<c>Quality</c> per item (no
/// COM out-parameters, which are painful to marshal late-bound). A bad item or a dropped connection
/// yields <see cref="TagReading.Bad"/> and drops the session so the next poll reconnects — the
/// <c>TagPump</c> already retries with backoff. NOT unit-testable without a live DA server; validate
/// on the OPC box.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ClassicDaTagSource : ITagSource, IDisposable
{
    private readonly string _progId;
    private readonly int _updateRateMs;
    private readonly object _gate = new();          // COM is synchronous + apartment-sensitive: serialize access
    private readonly Dictionary<string, object> _items = new(StringComparer.Ordinal);
    private dynamic? _server;
    private dynamic? _group;

    public ClassicDaTagSource(string progId, int updateRateMs = 500)
    {
        _progId = progId;
        _updateRateMs = updateRateMs;
    }

    public string Name => $"classic-da:{_progId}";

    public Task<IReadOnlyList<TagReading>> ReadAsync(IReadOnlyList<string> tags, CancellationToken ct)
    {
        lock (_gate)
        {
            try
            {
                return Task.FromResult(Read(tags));
            }
            catch
            {
                Reset();   // drop the session; the pump reconnects on the next poll
                return Task.FromResult((IReadOnlyList<TagReading>)tags.Select(TagReading.Bad).ToList());
            }
        }
    }

    private IReadOnlyList<TagReading> Read(IReadOnlyList<string> tags)
    {
        EnsureConnected();
        EnsureItems(tags);
        var now = DateTimeOffset.UtcNow;
        var readings = new List<TagReading>(tags.Count);
        foreach (var tag in tags)
        {
            if (!_items.TryGetValue(tag, out var handle)) { readings.Add(TagReading.Bad(tag)); continue; }
            try
            {
                dynamic item = handle;
                object? rawValue = item.Value;                            // cached from the subscribed group
                int quality = Convert.ToInt32((object)item.Quality, CultureInfo.InvariantCulture);
                var q = (quality & 0xC0) switch { 0xC0 => "Good", 0x40 => "Uncertain", _ => "Bad" };
                var value = rawValue is null ? null : Convert.ToString(rawValue, CultureInfo.InvariantCulture);
                readings.Add(new TagReading(tag, value, q) { At = now });
            }
            catch { readings.Add(TagReading.Bad(tag)); }
        }
        return readings;
    }

    private void EnsureConnected()
    {
        if (_server is not null && _group is not null) return;
        var progIdType = Type.GetTypeFromProgID("OPC.Automation.1")
            ?? throw new InvalidOperationException(
                "OPC.Automation is not registered — install the OPC Core Components Redistributable on this box.");
        _server = Activator.CreateInstance(progIdType)!;
        _server.Connect(_progId);                    // connect to the local DA server (e.g. CimQuestInc.IGOPCAB.1)
        _group = _server.OPCGroups.Add("abis-edge");
        _group.IsActive = true;
        _group.UpdateRate = _updateRateMs;
        _group.IsSubscribed = true;                  // keep each item's .Value fresh from the device
        _items.Clear();
    }

    private void EnsureItems(IReadOnlyList<string> tags)
    {
        foreach (var tag in tags)
        {
            if (_items.ContainsKey(tag)) continue;
            try
            {
                // OPCItems.AddItem(ItemID, ClientHandle) returns the item directly — no out-parameters.
                object item = _group!.OPCItems.AddItem(tag, _items.Count + 1);
                _items[tag] = item;
            }
            catch { /* an unknown item id just reads Bad; don't fail the whole group */ }
        }
    }

    private void Reset()
    {
        try { _server?.Disconnect(); } catch { /* best effort */ }
        _items.Clear();
        _group = null;
        _server = null;
    }

    public void Dispose() => Reset();
}
