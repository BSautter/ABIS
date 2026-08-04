import { describe, it, expect } from 'vitest';
import { decideConveyorWeight, SCALE_CELLS, MIN_SKID_LB, MAX_SKID_LB } from '../src/skid-weight.js';

/**
 * The rules deciding what may be written to a finished skid's NET weight.
 *
 * This is the highest-consequence logic in the client: the figure it produces lands in
 * `sheet_net_wt`, which invoicing and the 856 ASN are built from. Every rule below exists because
 * without it the button records a number the scale never gave.
 */
const cells = (occupancy: Record<number, boolean | null>) =>
  new Map(Object.entries(occupancy).map(([k, v]) => [Number(k), { location: Number(k), occupied: v, tags: [] }]));

const base = {
  lineName: 'BL 110',
  scaleTag: 'stacker110.ScaleSkidWt',
  conveyor: { reachable: true, configured: true, cells: cells({ 3: true, 4: false }) },
  stacker: { reachable: true, scaleWeight: 1200, via: '' },
};

describe('decideConveyorWeight', () => {
  it('accepts a real weight with a stack on the scale', () => {
    const d = decideConveyorWeight(base);
    expect(d).toEqual({ ok: true, netLb: 1200, via: '' });
  });

  it('accepts on either scale cell', () => {
    // Legacy read the scale at location 3 OR 4 — StackOnConveyor1 and StackLeavingConveyor1.
    for (const c of SCALE_CELLS) {
      const d = decideConveyorWeight({ ...base, conveyor: { ...base.conveyor, cells: cells({ [c]: true }) } });
      expect(d.ok).toBe(true);
    }
  });

  it('refuses when no stack is on the scale — the idle-belt case', () => {
    // This is the one that matters most: an idle BL 110 reads ScaleSkidWt = 0 with every cell clear,
    // verified live. Without this guard the button records 0 lb as a skid's net weight.
    const d = decideConveyorWeight({
      ...base,
      conveyor: { ...base.conveyor, cells: cells({ 3: false, 4: false }) },
      stacker: { reachable: true, scaleWeight: 0, via: '' },
    });
    expect(d.ok).toBe(false);
    expect(d.ok === false && d.reason).toContain('No stack on Conveyor 1');
  });

  it('treats an unreadable cell as NOT on the scale', () => {
    // occupied === null is an unreadable sensor. Treating unknown as "on the scale" is how you record
    // the weight of an empty belt.
    const d = decideConveyorWeight({ ...base, conveyor: { ...base.conveyor, cells: cells({ 3: null, 4: null }) } });
    expect(d.ok).toBe(false);
    expect(d.ok === false && d.reason).toContain('No stack');
  });

  it('refuses a line with no conveyor scale, and names it', () => {
    const d = decideConveyorWeight({ ...base, lineName: 'BL 78', conveyor: { ...base.conveyor, configured: false } });
    expect(d.ok).toBe(false);
    expect(d.ok === false && d.reason).toContain('BL 78');   // the operator is told which line
  });

  it('refuses when the edge cannot be reached at all', () => {
    const d = decideConveyorWeight({ ...base, conveyor: { ...base.conveyor, reachable: false } });
    expect(d.ok).toBe(false);
    expect(d.ok === false && d.reason).toContain('manually');   // every refusal says what to do instead
  });

  it('refuses without a per-line scale tag, so one line cannot read another\'s scale', () => {
    // The edge's /stacker defaults resolve to stacker110. A console left on the defaults would hand
    // BL 110's weight to another line's skid.
    const d = decideConveyorWeight({ ...base, scaleTag: '' });
    expect(d.ok).toBe(false);
    expect(d.ok === false && d.reason).toContain('scale tag');
  });

  it('refuses a null reading rather than treating it as zero', () => {
    // A configured tag reading Bad quality comes back null — BL 84's stripped OPC branch does exactly
    // this. Zero is a weight; unknown is not.
    const d = decideConveyorWeight({ ...base, stacker: { reachable: true, scaleWeight: null, via: '' } });
    expect(d.ok).toBe(false);
    expect(d.ok === false && d.reason).toContain('did not answer');
  });

  it('applies legacy\'s plausibility band at both ends', () => {
    // legacy: if ll_nw < 10 or ll_nw > 39000 then "Invalid weight!!"
    const at = (w: number) => decideConveyorWeight({ ...base, stacker: { ...base.stacker, scaleWeight: w } });

    expect(at(MIN_SKID_LB - 1).ok).toBe(false);
    expect(at(MIN_SKID_LB).ok).toBe(true);       // the bound itself is allowed, as legacy allowed it
    expect(at(MAX_SKID_LB).ok).toBe(true);
    expect(at(MAX_SKID_LB + 1).ok).toBe(false);
  });

  it('names the out-of-range figure so the operator can see what the scale said', () => {
    const d = decideConveyorWeight({ ...base, stacker: { ...base.stacker, scaleWeight: 91000 } });
    expect(d.ok === false && d.reason).toContain('91,000');
  });

  it('carries the fallback marker through, so the operator knows which host answered', () => {
    const d = decideConveyorWeight({ ...base, stacker: { ...base.stacker, via: ' (fallback)' } });
    expect(d.ok === true && d.via).toBe(' (fallback)');
  });

  it('checks the belt before the scale — an idle line is told to wait, not that the scale failed', () => {
    // Ordering matters for the message the operator gets. With no stack AND no reading, "no stack yet"
    // is the useful answer; "the scale did not answer" would send them looking for a fault.
    const d = decideConveyorWeight({
      ...base,
      conveyor: { ...base.conveyor, cells: cells({ 3: false, 4: false }) },
      stacker: { reachable: false, scaleWeight: null, via: '' },
    });
    expect(d.ok === false && d.reason).toContain('No stack on Conveyor 1');
  });
});
