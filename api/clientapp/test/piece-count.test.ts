import { describe, it, expect } from 'vitest';
import { piecesThisSkid, nextBaseline } from '../src/piece-count.js';

/**
 * The stacker piece count that auto-fills a skid.
 *
 * Skid pieces are a production figure: they reach the customer on a packing ticket and the 856 ASN,
 * and they feed invoicing. So the interesting cases are all the ones that must resolve to NULL —
 * null means the operator types it, which is the outcome that cannot be silently wrong.
 */
describe('piecesThisSkid', () => {
  it('is the delta from the baseline captured at the last save', () => {
    expect(piecesThisSkid(1250, 1000)).toBe(250);
    expect(piecesThisSkid(1000, 1000)).toBe(0);   // a fresh skid with nothing on it yet
  });

  it('is null when there is no CURRENT reading — a stale one must not be carried forward', () => {
    // The bug this module was extracted for. The console kept the last good reading when the edge
    // went unreachable, so a count minutes old auto-filled the next skid saved.
    expect(piecesThisSkid(null, 1000)).toBeNull();
  });

  it('is null when there is no BASELINE to measure from', () => {
    expect(piecesThisSkid(1250, null)).toBeNull();
    expect(piecesThisSkid(null, null)).toBeNull();
  });

  it('is null on a counter ROLLBACK rather than a negative count', () => {
    // The PLC counter was reset or wrapped. A negative delta is not a smaller number, it is a wrong
    // one — and `skidPieces: -80` would be accepted by the write path.
    expect(piecesThisSkid(40, 1000)).toBeNull();
  });

  it('a zero reading is a real count, not a missing one', () => {
    // 0 is falsy in JS, and treating it as absent is the classic way this goes wrong.
    expect(piecesThisSkid(0, 0)).toBe(0);
  });
});

describe('nextBaseline', () => {
  it('carries the current reading forward as the next skid zero point', () => {
    expect(nextBaseline(1250)).toBe(1250);
  });

  it('carries NULL forward when the counter was unknown at save time', () => {
    // Keeping the old baseline through an outage would make the next skid's delta span BOTH skids and
    // over-count it by roughly a whole skid — silently, and in the direction that over-bills.
    expect(nextBaseline(null)).toBeNull();
  });

  it('a zero reading becomes a zero baseline, not a null one', () => {
    expect(nextBaseline(0)).toBe(0);
  });
});
