import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// authFetch is the whole point of this module, so it is mocked rather than the global fetch: the bug
// this guards against was using <img src> and NOT going through authFetch at all.
const authFetch = vi.fn();
vi.mock('../src/auth.js', () => ({ authFetch: (...a: unknown[]) => authFetch(...a) }));

const { renderSketch } = await import('../src/sketch.js');

/**
 * The sketch renderer.
 *
 * This module exists because the obvious implementation is wrong in a way that looks like the feature
 * simply not working: `<img src="/api/sketches/1/image">` cannot carry X-Api-Key or a bearer token, so
 * it gets a 401 and leaves a broken-image icon on a production screen. I shipped exactly that in the
 * first draft of #350. These tests pin the behaviours that were expensive to learn.
 */
describe('renderSketch', () => {
  let target: { card: HTMLElement; meta: HTMLElement; img: HTMLImageElement; link: HTMLAnchorElement };
  let created: string[];
  let revoked: string[];

  beforeEach(() => {
    document.body.innerHTML = `
      <div id="card"><div id="meta"></div><a id="link"><img id="img" /></a></div>`;
    target = {
      card: document.getElementById('card')!,
      meta: document.getElementById('meta')!,
      img: document.getElementById('img') as HTMLImageElement,
      link: document.getElementById('link') as HTMLAnchorElement,
    };
    created = [];
    revoked = [];
    let n = 0;
    // jsdom has no object-URL implementation; these record what the module does with them, which is
    // what the leak test needs to observe.
    URL.createObjectURL = vi.fn(() => { const u = `blob:sketch-${++n}`; created.push(u); return u; });
    URL.revokeObjectURL = vi.fn((u: string) => { revoked.push(u); });
    authFetch.mockReset();
  });

  afterEach(() => vi.restoreAllMocks());

  const ok = () => ({ ok: true, status: 200, blob: async () => new Blob([new Uint8Array([0x42, 0x4d])]) });

  it('fetches through authFetch — never a bare <img src>', async () => {
    authFetch.mockResolvedValue(ok());
    await renderSketch(target, 7, 'BRKT-A', null);

    expect(authFetch).toHaveBeenCalledWith('/api/sketches/7/image');
    // The src must be the object URL, NOT the API path — an API path here is the 401 bug returning.
    expect(target.img.getAttribute('src')).toBe('blob:sketch-1');
    expect(target.img.getAttribute('src')).not.toContain('/api/');
  });

  it('hides the card entirely when the job has no sketch', async () => {
    await renderSketch(target, null);
    expect(target.card.hidden).toBe(true);
    expect(authFetch).not.toHaveBeenCalled();   // nothing to ask for
  });

  it('says so in words when the sketch exists but stores no drawing', async () => {
    authFetch.mockResolvedValue({ ok: false, status: 404 });
    await renderSketch(target, 2, 'PANEL-B', null);

    expect(target.card.hidden).toBe(false);           // the sketch exists…
    expect(target.img.hidden).toBe(true);             // …but there is no image to show
    expect(target.meta.textContent).toContain('No drawing stored');
  });

  it('reports a failure as a failure rather than as an empty frame', async () => {
    authFetch.mockResolvedValue({ ok: false, status: 500 });
    await renderSketch(target, 3, 'X', null);
    expect(target.meta.textContent).toContain('500');
    expect(target.img.hidden).toBe(true);
  });

  it('survives the request throwing', async () => {
    authFetch.mockRejectedValue(new TypeError('network'));
    await renderSketch(target, 4, 'X', null);
    expect(target.meta.textContent).toContain('could not be loaded');
  });

  it('revokes the previous drawing before replacing it', async () => {
    // A shop-floor console left open all shift would otherwise hold 417 KB for every job looked at.
    authFetch.mockResolvedValue(ok());
    await renderSketch(target, 1, 'A', null);
    await renderSketch(target, 2, 'B', null);

    expect(created).toEqual(['blob:sketch-1', 'blob:sketch-2']);
    expect(revoked).toContain('blob:sketch-1');
    expect(revoked).not.toContain('blob:sketch-2');   // the one on screen is still needed
  });

  it('revokes when moving to a job with no sketch at all', async () => {
    authFetch.mockResolvedValue(ok());
    await renderSketch(target, 1, 'A', null);
    await renderSketch(target, null);
    expect(revoked).toContain('blob:sketch-1');
  });

  it('shows the job-specific note, and escapes what it renders', async () => {
    authFetch.mockResolvedValue(ok());
    await renderSketch(target, 5, '<b>NAME</b>', 'cut to rev1');

    expect(target.meta.textContent).toContain('cut to rev1');
    // The NAME must be escaped, not interpreted — sketch names come from the database. Checking for
    // "no <b> anywhere" was wrong: the module renders its own <b>Drawing</b> label, so the assertion
    // has to be about the name's own markup surviving as text.
    expect(target.meta.innerHTML).toContain('&lt;b&gt;NAME&lt;/b&gt;');
    expect(target.meta.textContent).toContain('<b>NAME</b>');
  });

  it('points the full-size link at the drawing too', async () => {
    authFetch.mockResolvedValue(ok());
    await renderSketch(target, 6, 'A', null);
    // Same reason as the image: a link to /api/... would open an unauthenticated request.
    expect(target.link.getAttribute('href')).toBe('blob:sketch-1');
  });
});
