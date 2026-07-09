using System.Collections.Concurrent;
using System.IO.Ports;
using AbisEdge.Scales;
using AbisEdge.Tags;

// ABIS Edge — a small shop-floor service that reads weigh scales/gauges over
// serial AND line equipment over OPC, and exposes the latest values over HTTP,
// so the modern web stack can consume hardware the central API can't reach. It
// replaces the legacy `da` (WSC32 serial) + OPC integration. See docs/EDGE_SERVICE.md.

// Diagnostic one-shot for the Classic OPC DA path, run on the OPC box (Windows-only, COM):
//   AbisEdge --probe                       read the default items (Device110.spm/idle), verbose
//   AbisEdge --probe --browse [filter]     list every leaf tag id (optionally filtered)
//   AbisEdge --probe <item> [<item> ...]   read specific item ids, surfacing any COM error
if (args.Length > 0 && string.Equals(args[0], "--probe", StringComparison.OrdinalIgnoreCase))
{
    if (!OperatingSystem.IsWindows()) { Console.WriteLine("--probe requires Windows (COM)."); return; }
    ClassicDaProbe.Run(args.Skip(1).ToArray());
    return;
}

var builder = WebApplication.CreateBuilder(args);

// --- Configure the device from Edge:Scale:* (env or appsettings) ---------------
var scaleCfg = builder.Configuration.GetSection("Edge:Scale");
var provider = scaleCfg.GetValue("Provider", "Mock")!;

builder.Services.AddSingleton<IScale>(_ => provider.ToLowerInvariant() switch
{
    "serial" => new SerialScale(
        scaleCfg.GetValue<string>("Port") ?? throw new InvalidOperationException("Edge:Scale:Port is required for the serial provider."),
        scaleCfg.GetValue("BaudRate", 9600),
        Enum.Parse<Parity>(scaleCfg.GetValue("Parity", "None")!, ignoreCase: true),
        scaleCfg.GetValue("DataBits", 8),
        Enum.Parse<StopBits>(scaleCfg.GetValue("StopBits", "One")!, ignoreCase: true)),
    _ => new MockScale(scaleCfg.GetValue("Setpoint", 1234.5m), scaleCfg.GetValue("Unit", "LB")!),
});

builder.Services.AddSingleton<LatestReading>();
builder.Services.AddHostedService<ReadingPump>();

// --- Configure the OPC source from Edge:Opc:* ---------------------------------
var opcCfg = builder.Configuration.GetSection("Edge:Opc");
var opcProvider = opcCfg.GetValue("Provider", "Mock")!;
var opcTags = opcCfg.GetSection("Tags").Get<string[]>() ?? Array.Empty<string>();

// Line run-state: one tag whose value means running/stopped (drives the DAS PLC auto-downtime).
// Ensure it's in the polled set so /run-state has a fresh value even if it wasn't listed in Tags.
// Mode: Equals (a "running" boolean/word), NotEquals (an inverted signal like an idle bit), or
// GreaterThan (a numeric signal like strokes-per-minute > RunStateThreshold).
var runStateTag = opcCfg.GetValue<string?>("RunStateTag", null);
var runningValues = opcCfg.GetSection("RunningValues").Get<string[]>() ?? RunStateConfig.DefaultRunningValues;
Enum.TryParse<RunStateMode>(opcCfg.GetValue("RunStateMode", "Equals"), ignoreCase: true, out var runStateMode);
var runStateThreshold = opcCfg.GetValue("RunStateThreshold", 0.0);
if (!string.IsNullOrWhiteSpace(runStateTag) && !opcTags.Contains(runStateTag))
    opcTags = opcTags.Append(runStateTag).ToArray();

builder.Services.AddSingleton<ITagSource>(sp => opcProvider.ToLowerInvariant() switch
{
    "opcua" => new OpcUaTagSource(
        new OpcUaOptions(opcCfg.GetValue<string>("Endpoint")
                ?? throw new InvalidOperationException("Edge:Opc:Endpoint is required for the opcua provider."))
        {
            UseSecurity = opcCfg.GetValue("UseSecurity", false),
            AcceptUntrusted = opcCfg.GetValue("AcceptUntrusted", true),
            Username = opcCfg.GetValue<string?>("Username", null),
            Password = opcCfg.GetValue<string?>("Password", null),
        },
        sp.GetRequiredService<ILogger<OpcUaTagSource>>()),
    // Classic OPC DA read (INGEAR) via the OPC DA Automation wrapper — Windows-only (COM). This is
    // the "edge runs on the OPC box, reads DA locally" path; no UA bridge.
    "classicda" or "classic-da" => OperatingSystem.IsWindows()
        ? new ClassicDaTagSource(
            opcCfg.GetValue<string>("ProgId") ?? "CimQuestInc.IGOPCAB.1",
            opcCfg.GetValue("UpdateRateMs", 500))
        : throw new PlatformNotSupportedException("Edge:Opc:Provider=ClassicDa requires Windows (COM) — run it on the OPC box."),
    _ => new MockTagSource(),
});
builder.Services.AddSingleton(new TagSet(opcTags));
builder.Services.AddSingleton(new RunStateConfig(runStateTag, runningValues, runStateMode, runStateThreshold));
builder.Services.AddSingleton<LatestTags>();
builder.Services.AddHostedService<TagPump>();

// The ABIS DAS console (a different origin) polls /reading + /run-state from the browser, so the
// edge's read-only device endpoints must allow cross-origin GETs. This service exposes only
// read-only hardware values on a trusted plant LAN, so an open policy is acceptable.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.UseCors();

// Serve the DAS readout (wwwroot/index.html) at the root — the shop-floor live
// display of the scale + OPC tags, replacing the legacy `da`/DAS screen. Same
// origin as /reading + /tags, so no CORS or auth on the local edge host.
app.UseDefaultFiles();
app.UseStaticFiles();

// Liveness + which devices are wired up.
app.MapGet("/health", (IScale scale, ITagSource tags) =>
    Results.Ok(new { status = "ok", scale = scale.Name, opc = tags.Name }));

// The latest weight reading (503 until the device has produced one).
app.MapGet("/reading", (LatestReading latest) =>
    latest.Value is { } r ? Results.Ok(r) : Results.Json(new { status = "no-reading-yet" }, statusCode: 503));

// The latest OPC tag values (the configured Edge:Opc:Tags), and one by name.
app.MapGet("/tags", (LatestTags tags) => Results.Ok(tags.All()));
app.MapGet("/tags/{name}", (string name, LatestTags tags) =>
    tags.Get(name) is { } t ? Results.Ok(t) : Results.NotFound(new { tag = name, status = "no-value-yet" }));

// The line's run-state — the DAS console polls this (every ~3s) to auto-open/close a downtime
// instance. Pass ?tag=<item id> for a specific line (multiple lines = multiple run-state tags, all
// polled); omit to use the configured default RunStateTag. running: true=running, false=stopped,
// null=unknown (not configured, no value yet, or a bad read — the console must NOT act on null).
// Changed mode = running when the tag (e.g. a stroke counter) changed within RunStateThreshold sec.
app.MapGet("/run-state", (LatestTags tags, RunStateConfig cfg, string? tag) =>
{
    var t = string.IsNullOrWhiteSpace(tag) ? cfg.Tag : tag;
    if (string.IsNullOrWhiteSpace(t))
        return Results.Ok(new { configured = false, tag = (string?)null, value = (string?)null, quality = (string?)null, mode = cfg.Mode.ToString(), running = (bool?)null, at = (DateTimeOffset?)null });
    var r = tags.Get(t);
    var running = cfg.Mode == RunStateMode.Changed
        ? RunState.IsRunningByChange(tags.ChangedAt(t), r?.Quality, DateTimeOffset.UtcNow, cfg.Threshold)
        : RunState.IsRunning(r?.Value, r?.Quality, cfg);
    return Results.Ok(new
    {
        configured = true,
        tag = t,
        value = r?.Value,
        quality = r?.Quality,
        mode = cfg.Mode.ToString(),
        running,
        at = r?.At,
    });
});

// Discovery: browse the UA server's address space to find node ids (the wrapper's
// view of the INGEAR tags). Pass ?node=<id> to descend; omit for the Objects root.
// Only available on the OPC UA provider (the mock can't browse).
app.MapGet("/opc/browse", async (string? node, ITagSource tags, CancellationToken ct) =>
    tags is ITagBrowser browser
        ? Results.Ok(await browser.BrowseAsync(node, ct))
        : Results.Json(new { status = "browse-unavailable", source = tags.Name }, statusCode: 501));

app.Run();

/// <summary>The configured set of OPC tags to poll (from Edge:Opc:Tags).</summary>
public sealed record TagSet(IReadOnlyList<string> Tags);

/// <summary>Thread-safe holder for the most recent reading.</summary>
public sealed class LatestReading
{
    private volatile WeightReading? _value;
    public WeightReading? Value { get => _value; set => _value = value; }
}

/// <summary>Background pump: streams the scale into <see cref="LatestReading"/>,
/// reconnecting with backoff if the device drops.</summary>
public sealed class ReadingPump(IScale scale, LatestReading latest, ILogger<ReadingPump> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation("Edge reading pump started for {Device}", scale.Name);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await foreach (var reading in scale.ReadAsync(ct))
                    latest.Value = reading;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Scale {Device} read failed; retrying in 5s", scale.Name);
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) { break; }
            }
        }
    }
}

/// <summary>Thread-safe cache of the latest value per OPC tag, plus when each tag's value last
/// changed (for the Changed run-state mode — a stroke counter that stops climbing = a stopped line).</summary>
public sealed class LatestTags
{
    private readonly ConcurrentDictionary<string, TagReading> _byName = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _changedAt = new();

    public void Set(TagReading r)
    {
        // Record a change on the first sight of a tag, or when its (good) value differs from the last.
        if (!_byName.TryGetValue(r.Name, out var prev) || !string.Equals(prev.Value, r.Value, StringComparison.Ordinal))
            _changedAt[r.Name] = r.At;
        _byName[r.Name] = r;
    }

    public TagReading? Get(string name) => _byName.TryGetValue(name, out var r) ? r : null;
    public DateTimeOffset? ChangedAt(string name) => _changedAt.TryGetValue(name, out var t) ? t : null;
    public IReadOnlyCollection<TagReading> All() => _byName.Values.ToList();
}

/// <summary>Background pump: polls the configured OPC tags into <see cref="LatestTags"/>
/// on an interval, reconnecting with backoff on failure. No-op when no tags are
/// configured (so the mock/scaffold doesn't spin needlessly).</summary>
public sealed class TagPump(ITagSource source, TagSet set, LatestTags latest, ILogger<TagPump> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (set.Tags.Count == 0) { log.LogInformation("No OPC tags configured; tag pump idle."); return; }
        log.LogInformation("Edge tag pump started for {Source} ({Count} tags)", source.Name, set.Tags.Count);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var reading in await source.ReadAsync(set.Tags, ct))
                    latest.Set(reading);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "OPC {Source} read failed; retrying in 5s", source.Name);
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
