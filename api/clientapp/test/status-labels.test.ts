import { describe, it, expect, beforeEach } from 'vitest';
import {
  setLineNames, lineLabel, lineBoardRank, isDecommissionedLine, isProductionLine, NO_LINE_NUM,
} from '../src/status-labels.js';

/**
 * Line identity and the floor board's view of it.
 *
 * Three separate rules live here and they are deliberately NOT the same rule:
 *  - `lineLabel` answers "what does this line_num mean" and must answer for every line, including the
 *    NONE sentinel and a decommissioned one — a job row has to be able to say where it ran.
 *  - `isProductionLine` drops the `line_num = 0` sentinel from floor views. It once rendered as an
 *    eighth tile carrying 1,302 unassigned jobs and swamped the header totals.
 *  - `isDecommissionedLine` drops a line that no longer exists (BL 60, 2026-08-04) from floor views
 *    only. It keeps its production history, so reporting must still see it.
 */
describe('line identity and the floor board', () => {
  beforeEach(() => {
    setLineNames([
      { lineNum: 0, lineDesc: 'NONE' },
      { lineNum: 1, lineDesc: 'BL 24', displayOrder: 5 },
      { lineNum: 2, lineDesc: 'BL 36', displayOrder: 4 },
      { lineNum: 3, lineDesc: 'BL 60', decommissioned: true },
      { lineNum: 4, lineDesc: 'BL 78', displayOrder: 1 },
      { lineNum: 5, lineDesc: 'BL 108', displayOrder: 3 },
      { lineNum: 6, lineDesc: 'BL 110', displayOrder: 2 },
      { lineNum: 7, lineDesc: 'BL 84', displayOrder: 0 },
      { lineNum: 9, lineDesc: 'BL NEW' },   // real, but the plant has not placed it on the board
    ]);
  });

  it('resolves the real name from the LINE table, never "BL" + code', () => {
    // line_num is an internal CODE. "BL" + 4 would render "BL4", which is not a line that exists —
    // that was a real bug (#160, fixed #168).
    expect(lineLabel(4)).toBe('BL 78');
    expect(lineLabel(7)).toBe('BL 84');
    expect(lineLabel(4)).not.toBe('BL4');
  });

  it('keeps the space in the name exactly as production stores it', () => {
    // "BL 78", not "BL78" — the plant confirmed the raw value is kept.
    expect(lineLabel(4)).toContain(' ');
  });

  it('still names a decommissioned line, because history needs it', () => {
    expect(lineLabel(3)).toBe('BL 60');
  });

  it('orders the board the way the plant reads the floor', () => {
    const order = [0, 1, 2, 3, 4, 5, 6, 7, 9]
      .filter((n) => isProductionLine(n) && !isDecommissionedLine(n))
      .sort((a, b) => lineBoardRank(a) - lineBoardRank(b) || a - b)
      .map(lineLabel);

    expect(order).toEqual(['BL 84', 'BL 78', 'BL 110', 'BL 108', 'BL 36', 'BL 24', 'BL NEW']);
  });

  it('places an unlisted line AFTER the listed ones rather than losing it', () => {
    // A line the plant has not placed must still appear. Otherwise adding a line to the floor and
    // forgetting to update the order would make it invisible.
    expect(lineBoardRank(9)).toBeGreaterThan(lineBoardRank(1));
    expect(lineBoardRank(9)).toBe(Number.MAX_SAFE_INTEGER);
  });

  it('separates "not on the floor" from "not in the order"', () => {
    // BL 60 is decommissioned and unranked; BL NEW is unranked but live. Only one of them disappears.
    expect(isDecommissionedLine(3)).toBe(true);
    expect(isDecommissionedLine(9)).toBe(false);
    expect(lineBoardRank(3)).toBe(lineBoardRank(9));   // both unranked…
    // …yet only BL 60 drops out.
  });

  it('treats line 0 as a sentinel, not a press', () => {
    expect(isProductionLine(NO_LINE_NUM)).toBe(false);
    expect(lineLabel(NO_LINE_NUM)).toBe('NONE');   // but it still has a name
  });

  it('falls back to the raw code rather than inventing a name', () => {
    // If the map has not loaded, showing "BL 42" for an unknown code would be a lie.
    setLineNames([]);
    expect(lineLabel(42)).not.toContain('BL 42');
    expect(String(lineLabel(42))).toContain('42');
  });

  it('survives a lookup payload with missing or malformed rows', () => {
    setLineNames([
      { lineNum: 4, lineDesc: 'BL 78' },
      { lineNum: null, lineDesc: 'junk' },
      { lineNum: 5, lineDesc: null },
      // @ts-expect-error — the server should never send this, but a client must not throw on it.
      { },
    ]);
    expect(lineLabel(4)).toBe('BL 78');
    expect(() => lineLabel(5)).not.toThrow();
  });
});
