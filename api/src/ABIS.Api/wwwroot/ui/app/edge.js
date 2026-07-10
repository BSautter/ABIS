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
