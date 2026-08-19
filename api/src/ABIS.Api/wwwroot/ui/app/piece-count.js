// The stacker's running piece counter, turned into "pieces on the skid in progress".
//
// The stacker reports a free-running total that never resets, so a skid's count is a DELTA: the
// current reading minus a baseline captured when the last skid was saved. That delta auto-fills the
// skid's piece count on save, and skid pieces are a production figure — they reach the customer on a
// packing ticket and the 856 ASN, and they feed invoicing. A wrong one is not a display glitch.
//
// Which is why every uncertain case here resolves to NULL rather than a number. Null means the
// operator types the count, which is the outcome that cannot be silently wrong.
/**
 * Pieces on the skid in progress, or null when it cannot be known.
 *
 * Null on three distinct causes, all of which must not auto-fill:
 *
 *  - **no current reading** — the edge is unreachable, or the tag is unconfigured or reads Bad. The
 *    last good value is deliberately NOT carried forward: it may be minutes old and bears no relation
 *    to what is on the skid now.
 *  - **no baseline** — nothing has been saved since the job or the edge came back, so there is no
 *    zero point to measure from.
 *  - **a rollback** (current < baseline) — the PLC counter was reset or wrapped. The delta would be
 *    negative, and a negative piece count is not a smaller number, it is a wrong one.
 */
export function piecesThisSkid(current, baseline) {
    if (current == null || baseline == null)
        return null;
    const d = current - baseline;
    return d >= 0 ? d : null;
}
/**
 * The baseline to carry forward after a skid is saved.
 *
 * It becomes whatever the counter reads now — **including null**. That null is the point: if the
 * counter was unknown when the skid was saved, keeping the OLD baseline would make the next skid's
 * delta span both skids and over-count it, silently, by roughly a whole skid. A null instead
 * re-baselines on the next good reading, which undercounts nothing that can be known and leaves the
 * operator typing the one skid that straddled the outage.
 */
export function nextBaseline(current) {
    return current;
}
