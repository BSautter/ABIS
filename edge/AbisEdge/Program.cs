using System.Collections.Concurrent;
using System.IO.Ports;
using AbisEdge.Scales;
using AbisEdge.Tags;
using Microsoft.Extensions.Hosting.WindowsServices;

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

// A Windows Service starts in C:\Windows\System32, and WebApplication.CreateBuilder loads
// appsettings.json (the OPC-box config) relative to the content root *before* UseWindowsService()
// could re-point it — so when hosted as a service, pin the content root to the exe's folder up front.
// Off Windows / when not run by the SCM (dev, the Linux .deb) this is null = the default content root.
var contentRoot = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = contentRoot });
// Register with the Windows Service Control Manager when the SCM launches us (no-op otherwise), so
// `sc create` runs the edge as a proper service that responds to start/stop and restarts on reboot.
builder.Host.UseWindowsService();

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

// Stacker piece counter: the tag whose running value = pieces stacked on this line (the DAS console
// reads it to auto-fill pieces-per-skid). Ensure it's polled so /piece-count has a fresh value.
var pieceCountTag = opcCfg.GetValue<string?>("PieceCountTag", null);
if (!string.IsNullOrWhiteSpace(pieceCountTag) && !opcTags.Contains(pieceCountTag))
    opcTags = opcTags.Append(pieceCountTag).ToArray();

// Line production counters: good/reject pieces, press strokes, feed-length — the running PLC
// counters the DAS console baselines per coil run to show this run's production. Poll any set.
var countersCfg = new CountersConfig(
    opcCfg.GetValue<string?>("GoodCountTag", null),
    opcCfg.GetValue<string?>("RejectCountTag", null),
    opcCfg.GetValue<string?>("StrokeCountTag", null),
    opcCfg.GetValue<string?>("FeedLengthTag", null));
foreach (var t in countersCfg.Tags)
    if (!opcTags.Contains(t)) opcTags = opcTags.Append(t).ToArray();

// Line health/status: the direct run bit, the active-fault lamp, and the no-auto lockout flag.
var lineStatusCfg = new LineStatusConfig(
    opcCfg.GetValue<string?>("AutoRunningTag", null),
    opcCfg.GetValue<string?>("ActiveFaultTag", null),
    opcCfg.GetValue<string?>("NoAutoTag", null));
foreach (var t in lineStatusCfg.Tags)
    if (!opcTags.Contains(t)) opcTags = opcTags.Append(t).ToArray();

// Dual-station stacker: the two heads' live piece counters + stack-complete bits + the stacker scale.
var stackerCfg = new StackerConfig(
    opcCfg.GetValue<string?>("StackerStation1CountTag", null),
    opcCfg.GetValue<string?>("StackerStation2CountTag", null),
    opcCfg.GetValue<string?>("StackerStation1DoneTag", null),
    opcCfg.GetValue<string?>("StackerStation2DoneTag", null),
    opcCfg.GetValue<string?>("StackerScaleWeightTag", null),
    opcCfg.GetValue<string?>("StackerScaleSkidIdTag", null));
foreach (var t in stackerCfg.Tags)
    if (!opcTags.Contains(t)) opcTags = opcTags.Append(t).ToArray();

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
builder.Services.AddSingleton(new PieceCountConfig(pieceCountTag));
builder.Services.AddSingleton(countersCfg);
builder.Services.AddSingleton(lineStatusCfg);
builder.Services.AddSingleton(stackerCfg);
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

// The stacker's running piece count — the DAS console polls this to show the live count and auto-fill
// pieces-per-skid (the delta since the last skid is computed console-side). Pass ?tag=<item id> for a
// specific line's stacker; omit to use the configured default PieceCountTag. count: the counter as a
// whole number, or null = unknown (not configured, no value yet, or a bad/non-numeric read — the
// console must NOT auto-fill on null, it leaves the field for the operator).
app.MapGet("/piece-count", (LatestTags tags, PieceCountConfig cfg, string? tag) =>
{
    var t = string.IsNullOrWhiteSpace(tag) ? cfg.Tag : tag;
    if (string.IsNullOrWhiteSpace(t))
        return Results.Ok(new { configured = false, tag = (string?)null, value = (string?)null, quality = (string?)null, count = (long?)null, at = (DateTimeOffset?)null });
    var r = tags.Get(t);
    return Results.Ok(new
    {
        configured = true,
        tag = t,
        value = r?.Value,
        quality = r?.Quality,
        count = PieceCount.Parse(r?.Value, r?.Quality),
        at = r?.At,
    });
});

// The line's four running production counters in one read: good/reject pieces, press strokes, and
// feed-length — the raw cumulative PLC values (legacy goodpartcnt/rejectpartcnt/strokecnt/feedlength).
// The DAS console baselines these when a coil run opens and shows the delta as this run's production;
// the edge stays stateless and just reports what the PLC currently holds. An unconfigured counter
// reports value null (the console then hides that tile). ?good=/?reject=/?stroke=/?feed= override the
// configured tag ids (for the console's tag picker), mirroring /run-state and /piece-count.
app.MapGet("/counters", (LatestTags tags, CountersConfig cfg, string? good, string? reject, string? stroke, string? feed) =>
{
    object One(string? tagOverride, string? cfgTag, bool whole)
    {
        var t = string.IsNullOrWhiteSpace(tagOverride) ? cfgTag : tagOverride;
        if (string.IsNullOrWhiteSpace(t))
            return new { configured = false, tag = (string?)null, value = (double?)null, quality = (string?)null, at = (DateTimeOffset?)null };
        var r = tags.Get(t);
        // Whole counters (pieces/strokes) reuse the piece-count parse; feed-length keeps decimals.
        double? val = whole
            ? PieceCount.Parse(r?.Value, r?.Quality)
            : FeedLength.Parse(r?.Value, r?.Quality);
        return new { configured = true, tag = t, value = val, quality = r?.Quality, at = r?.At };
    }
    return Results.Ok(new
    {
        good = One(good, cfg.GoodTag, true),
        reject = One(reject, cfg.RejectTag, true),
        stroke = One(stroke, cfg.StrokeTag, true),
        feed = One(feed, cfg.FeedTag, false),
    });
});

// A line's live health/status: the run bit (autorunning), the active-fault lamp, and the no-auto
// lockout — the direct PLC bits behind the fault/health lamps and the auto-status controls. /run-state
// stays the authoritative running signal (stroke counter climbing); this covers what it doesn't.
// ?autorunning=/?fault=/?noauto= override the configured tags per line.
app.MapGet("/line-status", (LatestTags tags, LineStatusConfig cfg, string? autorunning, string? fault, string? noauto) =>
{
    object One(string? o, string? c, Func<string?, string?, bool?> parse)
    {
        var t = string.IsNullOrWhiteSpace(o) ? c : o;
        if (string.IsNullOrWhiteSpace(t)) return new { configured = false, tag = (string?)null, value = (bool?)null, raw = (string?)null, quality = (string?)null, at = (DateTimeOffset?)null };
        var r = tags.Get(t);
        return new { configured = true, tag = t, value = parse(r?.Value, r?.Quality), raw = r?.Value, quality = r?.Quality, at = r?.At };
    }
    return Results.Ok(new
    {
        running = One(autorunning, cfg.AutoRunningTag, StackDone.Parse),   // truthy bit = running
        fault = One(fault, cfg.ActiveFaultTag, StackDone.Parse),           // truthy/non-zero = a fault is active (raw carries the code)
        noauto = One(noauto, cfg.NoAutoTag, StackDone.Parse),              // truthy = the line is in manual / auto locked out
    });
});

// A line's two stacker stations in one read: each head's live piece counter + its stack-complete
// bit, plus the stacker scale (weight + skid id). The DAS console pairs the live count with the skid
// AT each head (LINE_CURRENT_STATUS.SHEET_SKID_STACKER_1/2, resolved by the API's line board). The
// edge stays stateless — raw current values only. ?s1count=/?s2count=/?s1done=/?s2done=/?scalewt=/
// ?scaleid= override the configured tags per line, mirroring /counters.
app.MapGet("/stacker", (LatestTags tags, StackerConfig cfg,
    string? s1count, string? s2count, string? s1done, string? s2done, string? scalewt, string? scaleid) =>
{
    object Count(string? o, string? c)
    {
        var t = string.IsNullOrWhiteSpace(o) ? c : o;
        if (string.IsNullOrWhiteSpace(t)) return new { configured = false, tag = (string?)null, count = (long?)null, quality = (string?)null, at = (DateTimeOffset?)null };
        var r = tags.Get(t);
        return new { configured = true, tag = t, count = PieceCount.Parse(r?.Value, r?.Quality), quality = r?.Quality, at = r?.At };
    }
    object Done(string? o, string? c)
    {
        var t = string.IsNullOrWhiteSpace(o) ? c : o;
        if (string.IsNullOrWhiteSpace(t)) return new { configured = false, tag = (string?)null, complete = (bool?)null, quality = (string?)null, at = (DateTimeOffset?)null };
        var r = tags.Get(t);
        return new { configured = true, tag = t, complete = StackDone.Parse(r?.Value, r?.Quality), quality = r?.Quality, at = r?.At };
    }
    return Results.Ok(new
    {
        station1 = new { count = Count(s1count, cfg.Station1CountTag), done = Done(s1done, cfg.Station1DoneTag) },
        station2 = new { count = Count(s2count, cfg.Station2CountTag), done = Done(s2done, cfg.Station2DoneTag) },
        scale = new
        {
            weight = Count(scalewt, cfg.ScaleWeightTag),   // reuse whole-number parse; skid weight is a count-scale integer
            skidId = Count(scaleid, cfg.ScaleSkidIdTag),
        },
    });
});

// Discovery: browse the OPC server's address space one level at a time to find item ids without
// hand-mapping them — so a tag picker (e.g. the DAS console choosing a line's piece-count tag) can
// point-and-click instead of someone RDP-ing to the box to run --probe --browse. Pass ?node=<id> to
// descend a branch; omit for the root. Works on the OPC UA and Classic-DA providers (and the mock's
// canned tree); a provider with no browse support → 501, a live browse failure → 502 (with the error).
app.MapGet("/opc/browse", async (string? node, ITagSource tags, CancellationToken ct) =>
{
    if (tags is not ITagBrowser browser)
        return Results.Json(new { status = "browse-unavailable", source = tags.Name }, statusCode: 501);
    try { return Results.Ok(await browser.BrowseAsync(node, ct)); }
    catch (Exception ex) { return Results.Json(new { status = "browse-failed", error = ex.Message }, statusCode: 502); }
});

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
