// Shared helpers for reading the ABIS shop-floor edge service (/run-state), used by both the DAS
// operator console and the operations dashboard. The two OPC boxes are a primary→fallback pair
// (.170 primary, .175 fallback) that both read the same presses, so a run-state read tries them in
// order and uses the first that responds — a .170 outage transparently uses .175 and recovers once
// .170 answers again. Plain fetch (the edge is unauthenticated + CORS-open on the plant LAN); a hung
// host is abandoned via an AbortController timeout so it can't stall the poll before we fail over.
//
// Note: the edge is HTTP. If a consumer page is ever served over HTTPS, the browser will block these
// requests as mixed content — the edge would then need to be fronted by TLS too.

export const DEFAULT_EDGE_URLS = 'http://192.168.10.170:8090, http://192.168.9.175:8090';

export interface RunStateResult {
  reachable: boolean;        // did any host respond at all?
  via: string;               // '' = primary answered, ' (fallback)' = a later host answered
  configured: boolean;       // the edge has a run-state tag for this query
  running: boolean | null;   // true / false / null (unknown — callers must never act on null)
}

/** Split a raw edge-URL string (comma/whitespace separated, primary first) into clean base URLs. */
export function parseEdgeUrls(raw: string): string[] {
  return raw.split(/[\s,]+/).map((u) => u.trim().replace(/\/$/, '')).filter(Boolean);
}

async function fetchWithTimeout(url: string, ms: number, init?: RequestInit): Promise<Response> {
  const ctl = new AbortController();
  const t = window.setTimeout(() => ctl.abort(), ms);
  try { return await fetch(url, { cache: 'no-store', signal: ctl.signal, ...init }); }
  finally { clearTimeout(t); }
}

export interface EdgeHostHealth { url: string; host: string; up: boolean; }

/**
 * Probe each edge host's /health (the shop-floor line/PLC feed). Used by the shell's notification bell
 * to alert when the floor feed is unreachable. A host that errors, times out, or answers non-OK = down.
 * Mixed-content note: an http edge host can't be probed from an https page — the caller filters those
 * out first so a browser block isn't mistaken for a down line.
 */
export async function probeEdgeHosts(bases: string[]): Promise<EdgeHostHealth[]> {
  return Promise.all(bases.map(async (url) => {
    let host = url;
    try { host = new URL(url).host; } catch { /* keep the raw url as the label */ }
    try { const r = await fetchWithTimeout(`${url}/health`, 2000); return { url, host, up: r.ok }; }
    catch { return { url, host, up: false }; }
  }));
}

/**
 * Read one tag's run-state across the edge hosts (primary first). The first host that responds wins;
 * a host that's unreachable, times out, or returns non-OK is skipped so we fail over to the next.
 * Only a non-response triggers failover — a responding host's own unknown/bad read is returned as-is.
 */
export async function fetchRunState(bases: string[], tag: string): Promise<RunStateResult> {
  const q = tag ? `?tag=${encodeURIComponent(tag)}` : '';
  for (let i = 0; i < bases.length; i++) {
    try {
      const r = await fetchWithTimeout(`${bases[i]}/run-state${q}`, 2000);
      if (!r.ok) continue;
      const s = await r.json() as { configured?: boolean; running?: boolean | null };
      return { reachable: true, via: i > 0 ? ' (fallback)' : '', configured: !!s.configured, running: s.running ?? null };
    } catch { /* unreachable/timeout → try the next host */ }
  }
  return { reachable: false, via: '', configured: false, running: null };
}

/** One node from the edge's /opc/browse — a branch (NodeClass "Object") or a leaf tag ("Variable"). */
export interface BrowsedTag { nodeId: string; displayName: string; nodeClass: string; }

export interface BrowseResult {
  reachable: boolean;   // did any edge host respond?
  supported: boolean;   // false = the provider can't browse (501: e.g. an unconfigured mock)
  nodes: BrowsedTag[];
  error?: string;       // a live browse failure (502 message) — distinct from an empty branch
}

/**
 * Browse one level of the edge OPC address space (primary→fallback) for tag discovery — powers the
 * DAS console's tag picker so an operator can pick a line's run-state / piece-count tag without
 * knowing its exact INGEAR item id. Pass `node` to descend a branch; omit for the root. Branches come
 * back NodeClass "Object" (descend into them), leaf tags "Variable" (a real item id to wire).
 */
export async function browseEdgeTags(bases: string[], node?: string): Promise<BrowseResult> {
  const q = node ? `?node=${encodeURIComponent(node)}` : '';
  for (let i = 0; i < bases.length; i++) {
    try {
      const r = await fetchWithTimeout(`${bases[i]}/opc/browse${q}`, 4000);
      if (r.status === 501) return { reachable: true, supported: false, nodes: [] };
      if (!r.ok) {
        let error = `HTTP ${r.status}`;
        try { const b = await r.json() as { error?: string }; if (b?.error) error = b.error; } catch { /* keep the status */ }
        return { reachable: true, supported: true, nodes: [], error };
      }
      const nodes = await r.json() as BrowsedTag[];
      return { reachable: true, supported: true, nodes: Array.isArray(nodes) ? nodes : [] };
    } catch { /* unreachable/timeout → try the next host */ }
  }
  return { reachable: false, supported: false, nodes: [], error: 'Edge unreachable' };
}

export interface PieceCountResult {
  reachable: boolean;      // did any host respond at all?
  via: string;             // '' = primary answered, ' (fallback)' = a later host answered
  configured: boolean;     // the edge has a piece-count tag for this query
  count: number | null;    // the stacker's running counter, or null (unknown — never auto-fill on null)
}

/**
 * Read the stacker's running piece counter across the edge hosts (primary first), same failover as
 * run-state. The counter is cumulative — the DAS console computes the per-skid delta from it. Returns
 * count=null on any non-response or a bad/non-numeric read so the console never auto-fills garbage.
 */
export async function fetchPieceCount(bases: string[], tag: string): Promise<PieceCountResult> {
  const q = tag ? `?tag=${encodeURIComponent(tag)}` : '';
  for (let i = 0; i < bases.length; i++) {
    try {
      const r = await fetchWithTimeout(`${bases[i]}/piece-count${q}`, 2000);
      if (!r.ok) continue;
      const s = await r.json() as { configured?: boolean; count?: number | null };
      return { reachable: true, via: i > 0 ? ' (fallback)' : '', configured: !!s.configured, count: s.count ?? null };
    } catch { /* unreachable/timeout → try the next host */ }
  }
  return { reachable: false, via: '', configured: false, count: null };
}

/** One running production counter as the edge reports it. `value` is null = unknown (unconfigured, or
 * a bad/non-numeric read) — the console then hides that counter rather than show a fabricated delta. */
export interface CounterReading { configured: boolean; value: number | null; }

export interface CountersResult {
  reachable: boolean;
  via: string;
  good: CounterReading;
  reject: CounterReading;
  stroke: CounterReading;
  feed: CounterReading;   // feed-length keeps decimals (inches), unlike the whole-number counts
}

const NO_COUNTER: CounterReading = { configured: false, value: null };

/**
 * Read the line's four running production counters (good/reject pieces, strokes, feed-length) in one
 * call across the edge hosts (primary→fallback, same as run-state/piece-count). All are cumulative on
 * the PLC; the DAS console captures a baseline at coil-run start and shows the delta as this run's
 * production. A null value (unreachable or bad read) means "unknown" — never a fabricated count.
 */
/** Derive a line's four counter tags from its RUN tag, which the console already holds per line.
 *
 * The plant's tag map is one PLC prefix per line with fixed member names — `PLC5-BL84.strokecnt`,
 * `.goodpartcnt`, `.rejectpartcnt`, `.feedlength` — so the prefix of the run tag identifies the line
 * and the rest follow. That avoids asking the operator for four more tags they would have to keep in
 * step with the one they already set.
 *
 * Returns null for a tag with no prefix, which makes the caller fall back to the edge's defaults —
 * the previous behaviour, and correct on a single-line box. */
export function countersForRunTag(runTag?: string | null): Record<'good' | 'reject' | 'stroke' | 'feed', string> | null {
  const dot = (runTag ?? '').lastIndexOf('.');
  if (dot <= 0) return null;
  const prefix = runTag!.slice(0, dot);
  return {
    good: `${prefix}.goodpartcnt`,
    reject: `${prefix}.rejectpartcnt`,
    stroke: `${prefix}.strokecnt`,
    feed: `${prefix}.feedlength`,
  };
}

/** Read the line's counters. PASS `tags` TO SCOPE THEM TO A LINE.
 *
 * Without them the edge answers with its CONFIGURED DEFAULTS, and on this plant both edge hosts
 * default to BL110 — so every line's panel showed BL110's production. Found live 2026-08-09:
 * `/counters` on .170 and .175 both returned `PLC5-BL110.goodpartcnt` whatever line was displayed.
 * Display-only, so nothing wrong was ever saved, but an operator on BL78 was reading BL110's numbers.
 *
 * Every other per-line reader here already takes tags — run-state, piece-count, line-status. This one
 * was the exception. */
export async function fetchCounters(
  bases: string[],
  tags?: Record<'good' | 'reject' | 'stroke' | 'feed', string> | null,
): Promise<CountersResult> {
  const q = tags
    ? `?good=${encodeURIComponent(tags.good)}&reject=${encodeURIComponent(tags.reject)}`
      + `&stroke=${encodeURIComponent(tags.stroke)}&feed=${encodeURIComponent(tags.feed)}`
    : '';
  for (let i = 0; i < bases.length; i++) {
    try {
      const r = await fetchWithTimeout(`${bases[i]}/counters${q}`, 2000);
      if (!r.ok) continue;
      const s = await r.json() as Record<'good' | 'reject' | 'stroke' | 'feed', { configured?: boolean; value?: number | null } | undefined>;
      const one = (c?: { configured?: boolean; value?: number | null }): CounterReading =>
        ({ configured: !!c?.configured, value: c?.value ?? null });
      return { reachable: true, via: i > 0 ? ' (fallback)' : '', good: one(s.good), reject: one(s.reject), stroke: one(s.stroke), feed: one(s.feed) };
    } catch { /* unreachable/timeout → try the next host */ }
  }
  return { reachable: false, via: '', good: NO_COUNTER, reject: NO_COUNTER, stroke: NO_COUNTER, feed: NO_COUNTER };
}

/** A line's live health bits (edge /line-status). `running`/`fault`/`noauto` are null when unknown
 * (unconfigured or a bad-quality read); `faultRaw` carries the fault CODE the PLC reports (the live
 * BL110 read was "68"), and `quality` is the underlying tag quality for the OPC health lamp. */
export interface LineStatusResult {
  reachable: boolean;
  via: string;
  running: boolean | null;
  fault: boolean | null;
  noauto: boolean | null;
  faultRaw: string | null;
  quality: string | null;
  configured: boolean;
}

/**
 * Read a line's health bits across the edge hosts (primary→fallback). Pass the line's tag ids to
 * scope it to that line; omit them to use the edge's configured defaults. Unreachable → everything
 * null, so the caller shows "unknown" rather than a fabricated healthy/faulted state.
 */
export async function fetchLineStatus(
  bases: string[], tags?: { autorunning?: string; fault?: string; noauto?: string },
): Promise<LineStatusResult> {
  const q = new URLSearchParams();
  if (tags?.autorunning) q.set('autorunning', tags.autorunning);
  if (tags?.fault) q.set('fault', tags.fault);
  if (tags?.noauto) q.set('noauto', tags.noauto);
  const qs = q.toString() ? `?${q}` : '';
  type Bit = { configured?: boolean; value?: boolean | null; raw?: string | null; quality?: string | null } | undefined;
  for (let i = 0; i < bases.length; i++) {
    try {
      const r = await fetchWithTimeout(`${bases[i]}/line-status${qs}`, 2000);
      if (!r.ok) continue;
      const s = await r.json() as { running?: Bit; fault?: Bit; noauto?: Bit };
      return {
        reachable: true, via: i > 0 ? ' (fallback)' : '',
        running: s.running?.value ?? null,
        fault: s.fault?.value ?? null,
        noauto: s.noauto?.value ?? null,
        faultRaw: s.fault?.raw ?? null,
        // Any bit's quality reflects the OPC link; prefer the fault tag's, then the others.
        quality: s.fault?.quality ?? s.running?.quality ?? s.noauto?.quality ?? null,
        configured: !!(s.running?.configured || s.fault?.configured || s.noauto?.configured),
      };
    } catch { /* unreachable/timeout → try the next host */ }
  }
  return { reachable: false, via: '', running: null, fault: null, noauto: null, faultRaw: null, quality: null, configured: false };
}

/** One stacker station's live state: the running piece count on the head, and whether the PLC says
 * the stack is complete. `count`/`complete` null = unknown (unconfigured or a bad read). */
export interface StackerStation { configured: boolean; count: number | null; complete: boolean | null; }

export interface StackerResult {
  reachable: boolean;
  via: string;
  station1: StackerStation;
  station2: StackerStation;
  scaleWeight: number | null;   // the stacker scale's last skid weight
  scaleSkidId: number | null;
}

const NO_STATION: StackerStation = { configured: false, count: null, complete: null };

type StackerTag = { configured?: boolean; count?: number | null; complete?: boolean | null } | undefined;

/**
 * Read a line's two stacker stations (live piece count + stack-complete) and the stacker scale, in
 * one call across the edge hosts (primary→fallback). The DAS console pairs each station's live count
 * with the skid AT that head from the line board. null values mean "unknown" — never fabricated.
 */
export async function fetchStacker(bases: string[], scaleWeightTag?: string): Promise<StackerResult> {
  const station = (s?: { count?: StackerTag; done?: StackerTag }): StackerStation => ({
    configured: !!(s?.count?.configured || s?.done?.configured),
    count: s?.count?.count ?? null,
    complete: s?.done?.complete ?? null,
  });
  for (let i = 0; i < bases.length; i++) {
    try {
      // The edge's /stacker defaults point at ONE line's branch (stacker110). Pass this line's scale
      // item id explicitly when the caller knows it, so a console on another line cannot read BL110's
      // scale by falling through to the default — the same reason /conveyor is asked per line.
      const qs = scaleWeightTag ? `?scalewt=${encodeURIComponent(scaleWeightTag)}` : '';
      const r = await fetchWithTimeout(`${bases[i]}/stacker${qs}`, 2000);
      if (!r.ok) continue;
      const s = await r.json() as {
        station1?: { count?: StackerTag; done?: StackerTag };
        station2?: { count?: StackerTag; done?: StackerTag };
        scale?: { weight?: StackerTag; skidId?: StackerTag };
      };
      return {
        reachable: true, via: i > 0 ? ' (fallback)' : '',
        station1: station(s.station1), station2: station(s.station2),
        scaleWeight: s.scale?.weight?.count ?? null, scaleSkidId: s.scale?.skidId?.count ?? null,
      };
    } catch { /* unreachable/timeout → try the next host */ }
  }
  return { reachable: false, via: '', station1: NO_STATION, station2: NO_STATION, scaleWeight: null, scaleSkidId: null };
}

/** One conveyor station's live sensor state. `occupied` null = unknown (no readable cell), which the
 * board must render as unknown and NEVER as clear — an unreadable sensor making an occupied station
 * look empty is exactly the failure that would send a fork truck to the wrong place. */
export interface ConveyorCell { location: number; occupied: boolean | null; tags: string[]; }

export interface ConveyorResult {
  reachable: boolean;
  via: string;
  configured: boolean;                     // the edge has cells mapped for this line
  cells: Map<number, ConveyorCell>;        // by DAS location code (0..18)
}

/**
 * Read a line's conveyor position sensors (edge `/conveyor`) across the hosts, primary→fallback.
 *
 * These are the physical cells along the stacker→wrapper path, keyed by the same location code as
 * `LINE_CURRENT_STATUS.SHEET_SKID_LOCATION_n`. They are the ONLY live source of where a stack is:
 * those DB columns are the legacy stacker station's own bookkeeping and sit empty on the live
 * database, so without the cells the conveyor board renders blank.
 *
 * Occupancy only — the cells say a stack is *there*, not *which skid it is*. Identity needs the
 * tracking state machine that owns those DB columns, and running a second copy of it would make the
 * modern stack a competing writer, so the board pairs live occupancy with skid identity only where
 * the DB happens to carry it.
 */
export async function fetchConveyor(bases: string[], line?: number | null): Promise<ConveyorResult> {
  const qs = line != null ? `?line=${encodeURIComponent(String(line))}` : '';
  for (let i = 0; i < bases.length; i++) {
    try {
      const r = await fetchWithTimeout(`${bases[i]}/conveyor${qs}`, 2000);
      if (!r.ok) continue;   // a 404 here = an edge build older than /conveyor; try the next host
      const s = await r.json() as { configured?: boolean; cells?: { location?: number; occupied?: boolean | null; tags?: string[] }[] };
      const cells = new Map<number, ConveyorCell>();
      for (const c of s.cells ?? []) {
        if (typeof c.location !== 'number') continue;
        cells.set(c.location, { location: c.location, occupied: c.occupied ?? null, tags: c.tags ?? [] });
      }
      return { reachable: true, via: i > 0 ? ' (fallback)' : '', configured: !!s.configured, cells };
    } catch { /* unreachable/timeout → try the next host */ }
  }
  return { reachable: false, via: '', configured: false, cells: new Map() };
}

/** What came back from asking the edge to re-zero its weigh device. */
export interface ZeroScaleResult {
  /** The command reached a device and was sent. Nothing more can be claimed — the scale does not
   *  acknowledge a zero, and legacy reads no reply either. */
  sent: boolean;
  /** A sentence for the operator. Never blank on failure: a zero that silently did nothing is the
   *  failure mode this whole path exists to avoid. */
  message: string;
}

/**
 * Ask the edge to re-zero the scale — legacy's `wf_zero_scale`, which sends a single `'a'` down the
 * COM port.
 *
 * The three edge answers are kept distinct, because they need different things from the operator:
 * the device cannot be commanded at all (409 — normal on a host whose skid weight is an OPC tag),
 * it can but the port is not open (503), or the command went out (200).
 *
 * **An unreachable edge is a failure, not a silent success.** Legacy returns success when its scale
 * is not connected; someone told the scale zeroed weighs against a tare that was never cleared, and
 * every skid on that scale is then wrong by the same amount.
 */
export async function zeroScale(bases: string[]): Promise<ZeroScaleResult> {
  for (let i = 0; i < bases.length; i++) {
    try {
      const r = await fetchWithTimeout(`${bases[i]}/scale/zero`, 4000, { method: 'POST' });
      const body = await r.json().catch(() => ({})) as { status?: string; device?: string; detail?: string };
      if (r.ok) return { sent: true, message: `Zero sent to ${body.device ?? 'the scale'}.` };
      // A reachable edge that refuses is an ANSWER — do not fall through to the next host and
      // report it as unreachable.
      return { sent: false, message: body.detail ?? `The scale was not zeroed (${body.status ?? r.status}).` };
    } catch { /* unreachable/timeout → try the next host */ }
  }
  return { sent: false, message: 'No edge service answered — the scale was NOT zeroed.' };
}
