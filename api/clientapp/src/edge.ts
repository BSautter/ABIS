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

async function fetchWithTimeout(url: string, ms: number): Promise<Response> {
  const ctl = new AbortController();
  const t = window.setTimeout(() => ctl.abort(), ms);
  try { return await fetch(url, { cache: 'no-store', signal: ctl.signal }); }
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
