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
/** Split a raw edge-URL string (comma/whitespace separated, primary first) into clean base URLs. */
export function parseEdgeUrls(raw) {
    return raw.split(/[\s,]+/).map((u) => u.trim().replace(/\/$/, '')).filter(Boolean);
}
async function fetchWithTimeout(url, ms) {
    const ctl = new AbortController();
    const t = window.setTimeout(() => ctl.abort(), ms);
    try {
        return await fetch(url, { cache: 'no-store', signal: ctl.signal });
    }
    finally {
        clearTimeout(t);
    }
}
/**
 * Probe each edge host's /health (the shop-floor line/PLC feed). Used by the shell's notification bell
 * to alert when the floor feed is unreachable. A host that errors, times out, or answers non-OK = down.
 * Mixed-content note: an http edge host can't be probed from an https page — the caller filters those
 * out first so a browser block isn't mistaken for a down line.
 */
export async function probeEdgeHosts(bases) {
    return Promise.all(bases.map(async (url) => {
        let host = url;
        try {
            host = new URL(url).host;
        }
        catch { /* keep the raw url as the label */ }
        try {
            const r = await fetchWithTimeout(`${url}/health`, 2000);
            return { url, host, up: r.ok };
        }
        catch {
            return { url, host, up: false };
        }
    }));
}
/**
 * Read one tag's run-state across the edge hosts (primary first). The first host that responds wins;
 * a host that's unreachable, times out, or returns non-OK is skipped so we fail over to the next.
 * Only a non-response triggers failover — a responding host's own unknown/bad read is returned as-is.
 */
export async function fetchRunState(bases, tag) {
    const q = tag ? `?tag=${encodeURIComponent(tag)}` : '';
    for (let i = 0; i < bases.length; i++) {
        try {
            const r = await fetchWithTimeout(`${bases[i]}/run-state${q}`, 2000);
            if (!r.ok)
                continue;
            const s = await r.json();
            return { reachable: true, via: i > 0 ? ' (fallback)' : '', configured: !!s.configured, running: s.running ?? null };
        }
        catch { /* unreachable/timeout → try the next host */ }
    }
    return { reachable: false, via: '', configured: false, running: null };
}
/**
 * Browse one level of the edge OPC address space (primary→fallback) for tag discovery — powers the
 * DAS console's tag picker so an operator can pick a line's run-state / piece-count tag without
 * knowing its exact INGEAR item id. Pass `node` to descend a branch; omit for the root. Branches come
 * back NodeClass "Object" (descend into them), leaf tags "Variable" (a real item id to wire).
 */
export async function browseEdgeTags(bases, node) {
    const q = node ? `?node=${encodeURIComponent(node)}` : '';
    for (let i = 0; i < bases.length; i++) {
        try {
            const r = await fetchWithTimeout(`${bases[i]}/opc/browse${q}`, 4000);
            if (r.status === 501)
                return { reachable: true, supported: false, nodes: [] };
            if (!r.ok) {
                let error = `HTTP ${r.status}`;
                try {
                    const b = await r.json();
                    if (b?.error)
                        error = b.error;
                }
                catch { /* keep the status */ }
                return { reachable: true, supported: true, nodes: [], error };
            }
            const nodes = await r.json();
            return { reachable: true, supported: true, nodes: Array.isArray(nodes) ? nodes : [] };
        }
        catch { /* unreachable/timeout → try the next host */ }
    }
    return { reachable: false, supported: false, nodes: [], error: 'Edge unreachable' };
}
/**
 * Read the stacker's running piece counter across the edge hosts (primary first), same failover as
 * run-state. The counter is cumulative — the DAS console computes the per-skid delta from it. Returns
 * count=null on any non-response or a bad/non-numeric read so the console never auto-fills garbage.
 */
export async function fetchPieceCount(bases, tag) {
    const q = tag ? `?tag=${encodeURIComponent(tag)}` : '';
    for (let i = 0; i < bases.length; i++) {
        try {
            const r = await fetchWithTimeout(`${bases[i]}/piece-count${q}`, 2000);
            if (!r.ok)
                continue;
            const s = await r.json();
            return { reachable: true, via: i > 0 ? ' (fallback)' : '', configured: !!s.configured, count: s.count ?? null };
        }
        catch { /* unreachable/timeout → try the next host */ }
    }
    return { reachable: false, via: '', configured: false, count: null };
}
const NO_COUNTER = { configured: false, value: null };
/**
 * Read the line's four running production counters (good/reject pieces, strokes, feed-length) in one
 * call across the edge hosts (primary→fallback, same as run-state/piece-count). All are cumulative on
 * the PLC; the DAS console captures a baseline at coil-run start and shows the delta as this run's
 * production. A null value (unreachable or bad read) means "unknown" — never a fabricated count.
 */
export async function fetchCounters(bases) {
    for (let i = 0; i < bases.length; i++) {
        try {
            const r = await fetchWithTimeout(`${bases[i]}/counters`, 2000);
            if (!r.ok)
                continue;
            const s = await r.json();
            const one = (c) => ({ configured: !!c?.configured, value: c?.value ?? null });
            return { reachable: true, via: i > 0 ? ' (fallback)' : '', good: one(s.good), reject: one(s.reject), stroke: one(s.stroke), feed: one(s.feed) };
        }
        catch { /* unreachable/timeout → try the next host */ }
    }
    return { reachable: false, via: '', good: NO_COUNTER, reject: NO_COUNTER, stroke: NO_COUNTER, feed: NO_COUNTER };
}
