import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { parseEdgeUrls, fetchStacker, fetchConveyor } from '../src/edge.js';

/**
 * The edge client — the browser's link to the two OPC boxes.
 *
 * Two things here are load-bearing on the shop floor:
 *  - the edge setting is a PRIMARY,FALLBACK pair, so the raw value is not a URL. Failing to split it
 *    means the console never reaches `.175` when `.170` is down.
 *  - `/stacker` defaults to ONE line's tags (stacker110). A console that does not name its line's
 *    scale tag would read BL 110's scale and put that weight on another line's skid.
 */
describe('parseEdgeUrls', () => {
  it('splits the primary,fallback pair the setting actually holds', () => {
    expect(parseEdgeUrls('http://192.168.10.170:8090, http://192.168.9.175:8090'))
      .toEqual(['http://192.168.10.170:8090', 'http://192.168.9.175:8090']);
  });

  it('keeps a single host working', () => {
    expect(parseEdgeUrls('http://192.168.10.170:8090')).toEqual(['http://192.168.10.170:8090']);
  });

  it('yields nothing for an unset or blank field, rather than a bogus host', () => {
    expect(parseEdgeUrls('')).toEqual([]);
    expect(parseEdgeUrls('   ')).toEqual([]);
    expect(parseEdgeUrls(',')).toEqual([]);
  });

  it('tolerates ragged spacing and trailing separators', () => {
    expect(parseEdgeUrls('  http://a:8090 ,, http://b:8090 , ')).toEqual(['http://a:8090', 'http://b:8090']);
  });
});

describe('fetchStacker', () => {
  const original = globalThis.fetch;
  beforeEach(() => { globalThis.fetch = vi.fn() as unknown as typeof fetch; });
  afterEach(() => { globalThis.fetch = original; vi.restoreAllMocks(); });

  const body = (weight: number | null) => ({
    ok: true,
    json: async () => ({
      station1: { count: { configured: true, count: 5 }, done: { configured: true, complete: false } },
      station2: { count: { configured: false }, done: { configured: false } },
      scale: { weight: { configured: true, count: weight }, skidId: { configured: true, count: 42 } },
    }),
  });

  it('passes the line\'s scale tag so the edge cannot answer with another line\'s scale', async () => {
    (globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValue(body(1234));
    await fetchStacker(['http://a:8090'], 'stacker110.ScaleSkidWt');

    const url = String((globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0][0]);
    expect(url).toContain('scalewt=stacker110.ScaleSkidWt');
  });

  it('omits the query entirely when no tag is given, rather than sending an empty one', async () => {
    (globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValue(body(1234));
    await fetchStacker(['http://a:8090']);
    expect(String((globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0][0])).not.toContain('scalewt=');
  });

  it('fails over to the second host and says it did', async () => {
    const f = globalThis.fetch as ReturnType<typeof vi.fn>;
    f.mockRejectedValueOnce(new TypeError('unreachable')).mockResolvedValueOnce(body(999));

    const r = await fetchStacker(['http://primary:8090', 'http://fallback:8090']);
    expect(r.reachable).toBe(true);
    expect(r.scaleWeight).toBe(999);
    expect(r.via).toContain('fallback');   // the operator is told which host answered
  });

  it('reports unreachable rather than inventing a reading when every host is down', async () => {
    (globalThis.fetch as ReturnType<typeof vi.fn>).mockRejectedValue(new TypeError('unreachable'));
    const r = await fetchStacker(['http://a:8090', 'http://b:8090']);

    expect(r.reachable).toBe(false);
    expect(r.scaleWeight).toBeNull();   // null = unknown, never 0 — 0 is a weight
  });

  it('keeps a null scale reading null', async () => {
    // A configured tag reading Bad quality comes back null. Turning that into 0 would be a weight the
    // scale never gave — BL 84's stripped branch reads exactly this way.
    (globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValue(body(null));
    const r = await fetchStacker(['http://a:8090'], 'stacker84.ScaleSkidWt');
    expect(r.scaleWeight).toBeNull();
  });
});

describe('fetchConveyor', () => {
  const original = globalThis.fetch;
  beforeEach(() => { globalThis.fetch = vi.fn() as unknown as typeof fetch; });
  afterEach(() => { globalThis.fetch = original; vi.restoreAllMocks(); });

  it('asks per line, because an unmapped line must not inherit another line\'s belt', async () => {
    (globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true, json: async () => ({ configured: true, cells: [{ location: 3, occupied: true, tags: ['x'] }] }),
    });
    const r = await fetchConveyor(['http://a:8090'], 6);

    expect(String((globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0][0])).toContain('line=6');
    expect(r.configured).toBe(true);
    expect(r.cells.get(3)?.occupied).toBe(true);
  });

  it('renders an unreadable cell as unknown, never as clear', async () => {
    // An unreadable sensor making an occupied station look empty is what sends a fork truck to the
    // wrong place.
    (globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true, json: async () => ({ configured: true, cells: [{ location: 4, occupied: null, tags: ['x'] }] }),
    });
    const r = await fetchConveyor(['http://a:8090'], 6);
    expect(r.cells.get(4)?.occupied).toBeNull();
  });

  it('reports not-configured for a line with no cell map', async () => {
    (globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true, json: async () => ({ configured: false, cells: [] }),
    });
    const r = await fetchConveyor(['http://a:8090'], 4);
    expect(r.configured).toBe(false);
    expect(r.cells.size).toBe(0);
  });
});
