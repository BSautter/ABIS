// Rendering a job's sketch — shared by every screen that shows one.
//
// This exists as a module rather than a snippet each page copies, because getting it wrong is easy and
// the failure is quiet. The obvious implementation, `<img src="/api/sketches/{id}/image">`, does NOT
// work: the endpoint sits behind the same auth as everything under /api, and a browser cannot put
// X-Api-Key or an Authorization bearer on an <img> request. A plain src attribute gets a 401 and leaves
// a broken-image icon on a production screen — it looks like the feature is missing, not like an auth
// problem. Confirmed against the running app: 401 unauthenticated, 200 image/bmp with a key.
//
// So the drawing is fetched like any other request and rendered from an object URL. The HTTP cache
// still applies (it is an ordinary GET, and the response carries a day's Cache-Control), so re-opening
// a job does not re-download the bitmap.
//
// The images are large — every one of the 128 on live .230 is a 417 KB uncompressed BMP — which is why
// object URLs are revoked before being replaced. A shop-floor console left open all shift would
// otherwise accumulate one per job the operator looks at.
import { authFetch } from './auth.js';

export interface SketchTarget {
  /** Container to show/hide depending on whether the job has a sketch at all. */
  card: HTMLElement;
  /** Element that receives the drawing's name and any job-specific note. */
  meta: HTMLElement;
  img: HTMLImageElement;
  /** Optional anchor for opening the drawing full size. */
  link?: HTMLAnchorElement | null;
}

const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));

/** Object URL currently displayed per <img>, so replacing a drawing releases the previous one. */
const shown = new WeakMap<HTMLImageElement, string>();

/**
 * Show `sketchId`'s drawing in `target`, or hide the card when the job has no sketch.
 *
 * Absence and failure are deliberately distinguished. A job with no sketch hides the card entirely; a
 * sketch row that exists but holds no image says so in words. Neither leaves an empty frame, which on a
 * production screen reads as something still loading or broken.
 */
export async function renderSketch(
  target: SketchTarget,
  sketchId?: number | null,
  sketchName?: string | null,
  jobNote?: string | null,
): Promise<void> {
  const prev = shown.get(target.img);
  if (prev) { URL.revokeObjectURL(prev); shown.delete(target.img); }

  if (sketchId == null) { target.card.hidden = true; return; }
  target.card.hidden = false;
  target.img.hidden = true;

  const meta = (extra = '') =>
    `<span><b>Drawing</b>${esc(sketchName || `#${sketchId}`)}</span>` +
    (jobNote ? `<span><b>Note for this job</b>${esc(jobNote)}</span>` : '') + extra;
  target.meta.innerHTML = meta();

  try {
    const r = await authFetch(`/api/sketches/${sketchId}/image`);
    if (r.status === 404) { target.meta.innerHTML = meta('<span class="muted">No drawing stored for this sketch.</span>'); return; }
    if (!r.ok) { target.meta.innerHTML = meta(`<span class="muted">Drawing unavailable (${r.status}).</span>`); return; }

    const url = URL.createObjectURL(await r.blob());
    shown.set(target.img, url);
    target.img.alt = `Sketch ${sketchName || sketchId}`;
    target.img.src = url;
    if (target.link) target.link.href = url;
    target.img.hidden = false;
  } catch {
    target.meta.innerHTML = meta('<span class="muted">Drawing could not be loaded.</span>');
  }
}
