using System.Globalization;
using System.Runtime.Versioning;
using TitaniumAS.Opc.Client.Common;
using TitaniumAS.Opc.Client.Da;

namespace AbisEdge.Tags;

/// <summary>
/// Reads Classic <b>OPC DA</b> items straight from a local DA server — the plant's INGEAR
/// <c>CimQuestInc.IGOPCAB.1</c> — using the typed <b>TitaniumAS.Opc.Client</b> library (the OPC DA
/// custom interface, not the automation wrapper). This is the chosen "PLC auto-downtime" path: run
/// the edge <b>on the OPC box</b> and read INGEAR locally (local COM, no DCOM, no UA bridge). Select
/// with <c>Edge:Opc:Provider=ClassicDa</c> + <c>Edge:Opc:ProgId</c>; item ids are INGEAR node paths,
/// e.g. <c>PLC5-BL84.strokecnt</c>.
///
/// <para><b>Windows-only at runtime</b> (COM), but the reference builds cross-platform (the ClassicDa
/// provider is only instantiated on Windows). Build the edge <b>x86</b> to match the 32-bit INGEAR
/// server. Reads are a typed <b>synchronous device read</b> (<see cref="OpcDaDataSource.Device"/>),
/// which forces INGEAR to poll the PLC now — no async subscription / message pump needed (a service
/// thread has none). A bad item / dropped session yields <see cref="TagReading.Bad"/> and drops the
/// session so the next poll reconnects (the <c>TagPump</c> retries with backoff).</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ClassicDaTagSource : ITagSource, IDisposable
{
    private readonly string _progId;
    private readonly object _gate = new();               // COM is apartment-sensitive: serialize access
    private readonly Dictionary<string, OpcDaItem> _items = new(StringComparer.Ordinal);
    private OpcDaServer? _server;
    private OpcDaGroup? _group;

    public ClassicDaTagSource(string progId, int updateRateMs = 500)
    {
        _progId = progId;
        _ = updateRateMs;   // retained for config compatibility; synchronous reads don't use a group update rate
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
        var toRead = tags.Where(_items.ContainsKey).Select(t => _items[t]).ToArray();

        var byId = new Dictionary<string, TagReading>(StringComparer.Ordinal);
        if (toRead.Length > 0)
        {
            foreach (var v in _group!.Read(toRead, OpcDaDataSource.Device))
            {
                var q = v.Quality.Master switch
                {
                    OpcDaQualityMaster.Good => "Good",
                    OpcDaQualityMaster.Uncertain => "Uncertain",
                    _ => "Bad",
                };
                var value = v.Value is null ? null : Convert.ToString(v.Value, CultureInfo.InvariantCulture);
                byId[v.Item.ItemId] = new TagReading(v.Item.ItemId, value, q) { At = now };
            }
        }

        return tags.Select(t => byId.TryGetValue(t, out var r) ? r : TagReading.Bad(t)).ToList();
    }

    private void EnsureConnected()
    {
        if (_server is { IsConnected: true } && _group is not null) return;
        _server = new OpcDaServer(UrlBuilder.Build(_progId, "localhost"));   // opcda://localhost/<progId>
        _server.Connect();
        _group = _server.AddGroup("abis-edge");
        _group.IsActive = true;
        _items.Clear();
    }

    private void EnsureItems(IReadOnlyList<string> tags)
    {
        var missing = tags.Where(t => !_items.ContainsKey(t)).ToArray();
        if (missing.Length == 0) return;
        var defs = missing.Select(t => new OpcDaItemDefinition { ItemId = t, IsActive = true }).ToArray();
        foreach (var r in _group!.AddItems(defs))
            if (r.Error.Succeeded) _items[r.Item.ItemId] = r.Item;   // an unknown item id is skipped → reads Bad
    }

    private void Reset()
    {
        try { _server?.Dispose(); } catch { /* best effort */ }
        _items.Clear();
        _group = null;
        _server = null;
    }

    public void Dispose() => Reset();
}
